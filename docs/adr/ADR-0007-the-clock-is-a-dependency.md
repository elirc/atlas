# ADR-0007: The Clock Is a Dependency, and Leave Balances Are Derived

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 06)
- **Deciders:** [Author], [Reviewer]

## Context

Two things collided in Sprint 06.

First, a bug report: *"A Philippines worker we hired on 2026-01-31 shows probation ending a day
before it actually does. Our Manila HR contact disagrees with the API."* Root cause: `Worker`'s
probation check read the server's **local** wall clock. Atlas runs US-hosted; when Manila's
calendar had already turned to the last day of the window, the server's date had not — so
`today < ProbationEndsOn` answered in the wrong timezone. The value was computed from a clock
nobody chose and nobody could control.

Second, the feature: leave **accrual** is date arithmetic — proration, month boundaries,
carryover at year end. All of it is business logic that only makes sense against a *chosen*
"now", and none of it has a defensible default.

Both problems are the same problem: **time was being read, not passed.**

## Decision 1: time is an input, enforced by a fitness function

Atlas already injects `TimeProvider` at the edge (since S01: `CreateClientOrg`, `HireWorker`,
… all take it by constructor). This ADR ratifies that as policy and adds teeth:

- Domain time reads are **banned**. `tests/Atlas.Domain.Tests/AmbientTimeBanTests.cs` scans
  every `src/Atlas.Domain/**/*.cs` file and fails the build on `DateTime.Now`, `DateTime.UtcNow`,
  `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` outside comments. The next convenient
  static-clock read is now a red build in this repo, not a support ticket.
- `Worker.IsInProbation()` (which read `DateTime.Now`) became `Worker.IsInProbationOn(DateOnly)`
  — pure. The Api derives that date from the injected `TimeProvider` (UTC) at the mapping edge.

> **Why a ban and not a patch?** Changing that one line to `UtcNow` would fix the symptom and
> leave the disease: the *next* person reaching for a static clock. A fitness function is how a
> team turns a one-time incident into a permanent property (the same move `ModelMappingTests`
> made in S02). Fitness functions grow from incidents — that is the pattern, not the exception.

### Honest post-mortem

The `DateTime.Now` lived in `src/Atlas.Domain/Workforce/Worker.cs`, introduced in Sprint 03 with
the workforce slice. It looked harmless — a convenience projection for "is this worker in
probation today?" — and the S03 tests, which asserted `ProbationEndsOn` (a pure value), never
exercised the wall-clock branch. It took a client in a distant timezone to surface it. No blame:
this is exactly how ambient time hides. The lesson is the fitness function, which would have
failed the S03 build had it existed.

### The residual we did NOT fix (deliberately)

`DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)` is still **UTC**, not the worker's own
calendar. For a Manila worker, "today" in UTC can differ from "today" in UTC+8. We chose UTC as
the least-wrong org-facing default — the same stance `GetWorkerTerms` already takes — because the
correct fix needs a worker timezone we do not yet model. Statutory dates that truly depend on the
worker's locale (probation, notice) will get the worker's calendar when we add it. This is called
out in the code (`DtoMapping`, `GetLeaveBalance`) and is a sprint exercise, so it cannot masquerade
as finished.

## Decision 2: accrual policy is chosen, written down, and tested

There is no "obvious" accrual math, so `LeaveAccrual` (pure) encodes explicit choices, each
documented in `docs/concepts/date-math.md`:

- **Accrual posts at the start of each month.** Asking on the 5th credits the whole month.
- **Only the hire month is prorated**, by **actual days** in that month (16th of a 31-day month →
  16/31), not a 30-day fiction.
- **Carryover cap is applied cap-then-accrue** at each leave-year boundary: stale days are
  forfeited *before* the new year earns, so a fresh year always has room for its full entitlement.
  (Accrue-then-cap is the stingier alternative; both orders are tested so the choice is visible.)
- **Country statutory minimums** live on the rule pack (`CountryRulePack.AnnualLeaveMinimumDays`);
  a `LeavePolicy` below its country's floor is refused at construction with the minimum named.

## Decision 3: the balance is derived, never stored (deviation from the playbook)

The Sprint 06 playbook proposed a middle path: store accrual **ledger rows** *and* a
**materialized balance**, reconciled by a test. We went simpler and record the deviation here.

**A leave balance is a pure function of `policy + hireDate + approved requests`.** There is no
balance column. "Spent" is the in-memory sum of the worker's approved requests; the balance is
recomputed on every read (`LeaveAccrual.BalanceOn`). This mirrors ADR-0004's stance exactly:
current terms are a *fold* over history, not a column, precisely because a derivable column that
must be maintained under concurrent writes is a consistency bug waiting for its moment. The
approved requests **are** the auditable ledger; there is nothing to reconcile because there is no
second copy to drift.

The Reviewer accepted this on one condition (see the PR debate): a **reconciliation property
test** pins the invariant — re-deriving the balance from the request ledger equals what the engine
returns, and available never exceeds `entitlement + carryover`. Cheap insurance that the "fold, not
column" claim stays true.

### One documented simplification

`BalanceOn` subtracts total spend *after* the accrual fold, which assumes spend draws down the
current leave year. Where a worker spent carried-over days that would otherwise have stayed under
the cap, this can over-forfeit. Per-leave-year spend attribution needs dated ledger buckets; we
defer it until a customer's policy makes the difference material. Called out in `LeaveAccrual`.

## Decision 4: approval is concrete now, extracted later

The leave approval flow (submitted → approved/denied + decider + timestamp) is inlined on
`LeaveRequest` on purpose. Sprint 08 introduces expenses as the **second** consumer, and *that* is
when a generic `ApprovalWorkflow<T>` gets extracted — on the second real use, not this speculation.
The shape here is the specification S08 will factor out (breadcrumb left in the code and the
debate). Copying once is cheaper than the wrong abstraction.

## Decision 5: worker self-service is deferred (honestly)

Employees cannot submit their own leave in v1 because **workers are not Atlas users** — there is no
worker↔user link to authorize self-service against. v1 leave is submitted, decided, and cancelled
**by HR/admin on behalf of** a worker. Inventing an identity mapping "sort of" now would bake in a
guess. Self-service lands when the worker↔user link is designed.

## Consequences

- Every "now" in the system is controllable; tests freeze `TimeProvider` and assert exact dates.
- Leave math is total and pure — table-driven tests cover proration edges and the year boundary.
- No stored balance means no drift and no reconciliation job; the cost is recomputation per read
  (bounded — a worker's approved-request set is small). If it ever hurts, a materialized read model
  is an S14-style projection, not a schema change.
- The ambient-time ban applies to **all** future domain code, including sprints not yet written.

## Rejected alternatives

- **Patch `DateTime.Now` → `DateTime.UtcNow` and move on.** Fixes one line, not the class of bug.
  The ban is the actual fix.
- **Full event sourcing of the leave balance** (accrual/spend event stream + replay): the
  projection/replay machinery is more than this product's questions need. We keep the ledger-shaped
  subset (immutable approved requests + a pure fold) without the engine.
- **Stored balance column + audit reconstruction:** the drift risk ADR-0004 already rejected for
  salary, for the same reasons.
- **Ledger rows + materialized balance reconciled by a test** (the playbook's middle path): still
  two copies of one truth. Our approved-requests ledger already IS the single copy; a reconciliation
  *property* test gives the safety without the second column.
- **A leave-year "bucket" table for per-year spend:** the correct model for the deferred
  simplification, but premature until a customer's carryover policy makes it pay for itself.
