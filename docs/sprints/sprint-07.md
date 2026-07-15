# Sprint 07 — The Payroll Engine (Business-Logic Flagship #2)

**Goal:** gross-to-net through country rule packs, executed in cycles, idempotently. The sprint
every earlier sprint was secretly preparing for.

## Scope

**In:** PayCycle (country + frequency + period) and PayrollRun state machine
(`draft → review → approved → executed`); gross-to-net calculation: terms-as-of(cycle period)
(S3) × rule pack (S4) × approved unpaid leave deductions (S6) → payslip lines; **penny-allocation
arc** (in-sprint RED→GREEN): splitting employer cost across payslip lines loses cents → allocate
via largest-remainder; Payslip persistence (immutable once run executes); idempotent run
execution (natural key `cycleId+period`, re-execution returns the existing run); run/payslip
endpoints + review payload; `docs/http/payroll.http`.

**Out:** actual disbursement (S9 introduces the payment provider; payroll "executes" to records),
off-cycle runs, corrections/rollbacks (S15 runbook material).

## Teaching focus

Composition of everything so far — temporal terms, Money, rule packs, leave — into one
calculation with an audit-grade paper trail. Idempotency as a *design input*, not a retrofit.

## Commit narrative (sketch)

1. `feat(domain): PayCycle + PayrollRun state machine (pure)`
2. `feat(domain): gross-to-net calculator over rule packs` (+ golden-file tests per country)
3. RED: `test(domain): payslip lines must sum to employer total` — fails on naive per-line rounding
4. fix: largest-remainder allocation on Money (the arc's payoff)
5. `feat(application): prepare/review/approve/execute use cases — execute idempotent by natural key`
6. `feat(infrastructure+api): persistence, endpoints`
7. `docs: ADR-0008 payroll composition & idempotent execution, concepts/money-allocation.md`

## Teaching placements

- 📘 on the calculator: composition root of the domain — S3/S4/S6 meet here; diagram in one-pager.
- 📘 on golden files: when example-based beats property-based (auditors think in examples).
- 🔍 on the state machine: "find who is allowed to call `execute` — authorization follows state."
- ⚠️ on immutability of executed payslips: corrections are new documents, never edits (accounting 101).
- 🔗 idempotent-execute ← foreshadow S10 middleware ("this pattern becomes infrastructure next").

## Debate

`[Reviewer]`: "The calculator takes 5 inputs; make it a class with DI like everything else."
`[Author]`: it's a *pure function* — inputs in, payslip out — and its testability IS the feature;
DI would let state leak in. The rule: stateless domain calculations stay static/pure; stateful
workflows get objects. Reviewer pushes successfully for a `PayrollCalculationInput` parameter
object though — follow-up commit collapses the arg list.

## Exercises

- Add a 13th-month pay rule for PHL (statutory) via the rule pack. Where does it surface in the run?
- Delete the largest-remainder allocator and re-run the property test 100 times. Read the failures.
- An HR manager edits a worker's salary mid-period. What does the *current* code pay? Is that right?
  Write the test that pins your answer.
