# ADR-0008: Payroll Composition, Penny Allocation, and Idempotent Execution

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 07)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 07 is where three earlier sprints converge into one calculation: S03's effective-dated terms,
S04's `Money` + country rule packs, and S06's approved leave become a worker's payslip. Payroll also
introduces a workflow (draft → review → approve → execute) and a hard operational requirement:
running the same period twice must not pay anyone twice. This ADR records the shape decisions that
made all of that safe.

## Decision 1: the calculator is a pure function taking a parameter object

`PayrollCalculator.Calculate(PayrollCalculationInput)` is a **static, pure** function — inputs in, a
`PayslipCalculation` out, no clock, no repository, no ambient state. Its testability is the feature:
golden-file tests pin exact numbers precisely because nothing can leak in to change them.

The Reviewer pushed to make it a DI class "like everything else" and **lost** — stateless domain
calculations stay pure; stateful workflows (the `PayrollRun` aggregate) get objects. But the Reviewer
**won** the second point: four positional arguments (two of them easy to transpose) is a latent bug,
so the arguments collapsed into a named `PayrollCalculationInput`. A parameter object groups inputs;
it does not smuggle in dependencies. The rule, written down: *group arguments, don't inject state.*

## Decision 2: "terms as of the period end" govern — no proration in v1

The calculator resolves the worker's terms with `EmploymentTerms.Resolve(history, period.End)` and
pays the **whole period** at them. A mid-period raise pays the *new* salary for the entire period; it
is not split. This settles S03's "derivation stays cheap" promise in code: the full history is passed
in and folded in memory — no stored "current salary", no extra query.

- **Rejected (deferred): proration of mid-period changes.** Paying part of a month at the old rate and
  part at the new is real payroll work, but it multiplies the day-count conventions we would have to
  choose and test, and every country wants a different one. v1 pays period-end terms and says so; the
  test `TermsAsOfPeriodEndTests` pins the answer so it can never be an accident. Proration is a future
  workflow, not a bug.

## Decision 3: annual figures are allocated across periods, largest-remainder

Statutory figures and salaries are **annual**; a monthly payslip is one twelfth. Rounding each twelfth
independently loses cents over the year (€100,000 / 12 = 8,333.33…, and 8,333.33 × 12 = 99,999.96).
So the calculator rounds the annual figure once and then `Money.Allocate(12)` splits it into twelve
shares that sum to **exactly** the annual amount (largest-remainder: leftover cents go to the earliest
periods). This was an in-sprint RED→GREEN arc: the property test "twelve periods sum to annual" failed
on the naive per-period rounding and passed once allocation replaced it. (See
`docs/concepts/money-allocation.md`.)

## Decision 4: idempotent execution — natural key + terminal state

A run's identity is its **natural key** `(PayCycleId, PeriodStart)`, unique in the database. Two
consequences make running payroll safe to retry:

- **Preparing** the same period returns the *existing* run and writes nothing. The friendly path is a
  lookup by natural key; the unique index is the guarantee under a concurrent double-submit.
- **Executing** an already-executed run is a no-op that succeeds — no second state write, no second
  audit row, no second event. `Executed` is terminal (the state machine has no move out of it).

"Executed" means the run is **recorded as final**, not that money moved. Disbursement is S09's payment
provider. Separating "the payroll is final" from "the bank was told to pay" lets each be idempotent on
its own terms — and this whole pattern is what S10 promotes into idempotency-key middleware.

- **Status choice for prepare:** a fresh run returns `201 Created`; an already-existing one returns
  `200 OK` with the same body. The status is how a client learns which happened without diffing the
  body. We rejected "always 200" (hides whether work was done) and "409 Conflict on re-prepare"
  (idempotency is not a conflict — it is the feature).

## Decision 5: executed payslips are immutable — corrections are new documents

`Payslip` and `PayslipLine` have **no mutators** — enforced at compile time and pinned by
`PayslipImmutabilityTests`. A wrong number is not edited in place; the run that produced it is wrong,
and the fix is a *new* run (the corrections runbook is S15). Editing a payslip would erase the evidence
of what was paid and believed — the opposite of an audit-grade paper trail.

## Consequences

- The calculator is trivially testable and reused unchanged by every use case and every country pack.
- Run totals are folded in the **Domain** (`PayrollRun.Total*()`), never `SUM()`'d in SQL — decimal
  cannot be aggregated on SQLite, and this keeps a single rounding/allocation policy.
- The list of runs is deliberately payslip-free (cheap index); the detail view eager-loads the graph.
  If that ever hurts, an S14 read-model is the answer, not a partial load that leaves totals lying.

## Notes / recorded bets

- `PeriodsPerYear = 12` is hard-coded for Monthly-only v1; a second cadence moves it onto the input.
- "Active worker" = hired on/before period end; when S05 engagement states integrate, this tightens to
  "engagement Active on the period."
- The employee-side deductions added to `CountryRulePack` this sprint are illustrative teaching values,
  not tax advice — the same caveat S04 recorded for the employer burden.
