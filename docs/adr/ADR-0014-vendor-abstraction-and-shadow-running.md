# ADR-0014: A Payroll-Calculation Vendor Port + Shadow Running

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 12)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

A mid-quarter requirement landed cold: Atlas signed **LedgerPay**, a second payroll-calculation
vendor, and some client orgs must switch to it **mid-quarter** (their current engine is being
deprecated for DEU). Three demands rode along: (1) per-org, per-country vendor selection,
effective-dated so a cycle in flight finishes on the old vendor; (2) LedgerPay returns different
line granularity, but our payslips must stay stable for consumers; (3) finance wants a **shadow-run
mode** that compares both vendors before any org flips.

This is the sprint the architecture has been underwriting since S01. The question is not "how do we
call LedgerPay" — that is one ACL. It is "**how local is the diff when a core vendor is swapped**",
and the PR body answers it with numbers (the locality report). This ADR records the two design moves
that made the diff local, and the debate that shaped the riskiest one.

## Decision 1: extract `IPayrollCalculationVendor`; the native engine becomes the first adapter

The pure `PayrollCalculator` (S07) is wrapped, not rewritten. `IPayrollCalculationVendor`
(Application) is the third vendor port in Atlas (`IFxRates` ADR-0005, `IPaymentProvider` ADR-0010
were the first two); `AtlasNativeCalculator` (Infrastructure) delegates straight to the pure engine,
and `LedgerPayCalculator` is the second implementation behind an ACL.

- **The golden files did not move.** `PayrollCalculatorGoldenTests` never appears in the diff. That
  untouched-and-green file IS the proof the extraction was behaviour-preserving — the third time this
  course applies the rule "a refactor whose characterization tests appear in the diff is not a
  refactor" (ADR-0009). Extraction over prediction, one more time.
- **A `PayrollVendorDirectory` maps the enum to the adapter.** A run pins a `PayrollVendor`; the
  directory resolves it. A missing adapter fails loud (a wiring bug is a 500), never a silent
  "compute nothing."

## Decision 2: LedgerPay behind an ACL — the PayGrid pattern, second application

`LedgerPayCalculator` is the only place that speaks LedgerPay's dialect: integer minor units, half-
away-from-zero rounding, coarse **bundled** lines (one `EE_STATUTORY`, one `ER_ONCOST`), an open,
versioned-without-notice line vocabulary. Above it, the word "LedgerPay" never appears. This is
ADR-0010's decisions replayed for a calculation vendor:

- **Different line granularity stops at the adapter.** LedgerPay's single bundled deduction becomes
  one canonical `EmployeeDeduction` line; the payslip that reaches persistence is the same SHAPE
  Atlas always stored. "Our payslips stay stable for consumers" is an ACL guarantee, not an API one.
- **The error asymmetry is preserved.** A country LedgerPay cannot price is a business "no" (a clean
  422). An unmapped line code is a hole in our model of the vendor (a loud 500,
  `UnmappedVendorLineException` — deliberately absent from the exception handler's table). We did NOT
  unify this with the payments-side `UnmappedVendorStateException`: the message is the value, and a
  shared base to save four lines would couple two ACLs at the seam they exist to keep apart (the
  YAGNI ledger from ADR-0009; revisit on a third vendor ACL).
- **The adapter's tests are the vendor's manual.** `LedgerPayDialectTests` drives it through bundled
  lines, minor-unit translation, an unsupported country, and a surprise code — and pins the
  DIVERGENCES from the native engine (LedgerPay over-withholds above a wage cap it ignores).

## Decision 3: effective-dated `VendorAssignment` + pin at run CREATION (the cycle-in-flight rule)

Vendor choice is per-org, per-country, and **effective-dated** — S03's `EmploymentRecord` pattern,
second application: append-only, no `EffectiveTo`, "current vendor" is a resolution
(`VendorSelection.Resolve`) not a column (ADR-0004).

The rule that makes a mid-quarter switch safe: **a run pins its vendor at CREATION, not execution.**
`PreparePayrollRun` resolves the vendor once, as of the creation day, and stores it on
`PayrollRun.SelectedVendor`. A flip effective tomorrow does not touch a run created today — that run
executes on the vendor whose numbers a reviewer approved; the flip is picked up by the NEXT period's
run. Resolving at execution, or per-worker, would let a switch silently swap the engine under an open
cycle. This is the ⚠️ teaching placement, and `PreparePayrollRunVendorTests` pins it.

## Decision 4: shadow running — gated, time-boxed, and redacted (the Reviewer's two wins)

`ShadowComparePayrollRun` recomputes an already-prepared run with the vendor the org is about to
switch to, diffs it worker-by-worker against the stored figures, and persists a comparison report.
The Reviewer's objection — "this doubles payroll compute and stores a second copy of sensitive
results" — was right on both counts, and both changes landed (the course's **third Reviewer win**;
see the PR debate thread and `meta/debate.md`):

- **Gated + time-boxed.** `IShadowRunPolicy` is an allowlist with a per-org expiry (`ShadowRunSettings`,
  default empty → shadow mode OFF). The second gate lives in the use case: there must be a **pending**
  switch to trial (`VendorSelection.PendingSwitch`), so shadowing **auto-expires** the day the flip
  becomes effective. Both gates must hold, so an enrolled org whose flip already happened stops on its
  own, and an un-enrolled org never starts. The compute doubles only where a migration is actually in
  flight.
- **Redacted to deltas.** `VendorComparison.DeltaOf` is the one function that ever holds both vendors'
  absolute net at once, and it returns signed **minor-unit deltas**. The report and its DTO have NO
  field that could carry a salary — redaction is a type shape, not a rule to remember. Finance learns
  *where* and *by how much* two vendors disagree without a duplicate payroll ledger to leak. A happy
  side effect: deltas are integers, so the summary counters sidestep the "decimal can't be SUM'd on
  SQLite" trap entirely.

A divergence beyond tolerance publishes `payroll.shadow_divergence_detected` (S10 outbox ordering),
so finance is told before it flips. **Automatic reconciliation is out of scope** — the report is for
a human; resolving a divergence is a decision, not a job.

## Consequences

- Swapping or adding a payroll vendor touches `IPayrollCalculationVendor`'s implementations and the
  directory registration — the prepare/shadow use cases cannot tell one vendor from another.
- A run records its own vendor forever (provenance a regulator can ask about), and re-preparation is
  idempotent against a switch (the pin is authoritative).
- Shadow mode is a real "run both, compare, then flip" migration tool, off by default, self-limiting,
  and safe to leave in the codebase.
- The measurement lives in the PR body: files-touched-per-layer, and every place locality broke.

## Notes / recorded bets

- **S1's bet is collected here with data.** ADR-0001 accepted nine projects for a two-table skeleton
  "invoiced against S11/S12 where change-locality is measured." The receipt is in `meta/pr-body.md`:
  the Domain change for a vendor swap is small and additive; the Infrastructure (adapters) took the
  weight, exactly where the hexagon says volatility should live.
- **Vendor is on the run, not on every payslip.** v2 payslips do NOT carry a vendor field; provenance
  lives one hop away on `PayrollRunDto.SelectedVendor`. Denormalising it onto `Payslip` would have
  rippled through its factory, mapping, migration, and golden tests — a locality break we declined.
- **One cadence still.** v1 is Monthly-only; both vendors divide the year 12 ways. A second cadence
  moves `PeriodsPerYear` onto the input (flagged since ADR-0008) and both adapters follow.
