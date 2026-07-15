# Sprint 06 — Time Off: Accruals, Proration, Country Minimums

**Goal:** leave balances that survive an accountant's audit — and the sprint where the course's
clock discipline gets forged in fire (**harvest: the date-math bug planted in S03**).

## Scope

**In:** LeavePolicy (annual entitlement, accrual cadence, carryover cap) with country-minimum
enforcement from rule packs; LeaveBalance with monthly accrual + proration for mid-month starts;
LeaveRequest lifecycle (draft → submitted → approved/denied → cancelled) with overlapping-request
and insufficient-balance guards; approval flow v1 (HR Manager approves — deliberately concrete,
extraction happens S8); **`IClock` port introduced properly** + `DateOnly` sweep; leave endpoints
+ balance view; `docs/http/leave.http`.

**Out:** generic Approval component (S8 extracts it), public holidays calendars.

## Teaching focus

Date arithmetic is business logic, not utility code. The clock as a dependency. Proration and
rounding rules must be *chosen*, written down, and tested — there is no default.

## The harvest arc (ledger #2) — in-PR commits

1. "Client report: probation end wrong for PHL workers hired end-of-month" — **RED test commit**:
   fake clock at `2026-01-31T23:30` server local (UTC-8) vs worker in UTC+8 — probation end lands
   a day early. Root cause on display: `DateTime.Now` inside S03's probation computation.
2. **Fix commit:** `IClock` injected everywhere time is read; probation/notice math moves to
   `DateOnly` in the *worker's* calendar; audit of every `DateTime.Now`/`UtcNow` in the codebase
   (grep list in commit body).
3. Accrual engine built ON the fixed foundation (it needs the same discipline).

## Commit narrative (sketch)

1. RED: probation date-math failing test (the bug report)
2. fix: IClock port + DateOnly sweep (harvest #2)
3. `feat(domain): LeavePolicy + accrual/proration engine (pure)` + property tests
4. `feat(domain): LeaveRequest lifecycle + guards`
5. `feat(application+infrastructure+api): use cases, persistence, endpoints`
6. `docs: ADR-0007 the clock is a dependency, concepts/date-math.md`

## Teaching placements

- 📘 on IClock: controllable time; why static clocks make tests lie.
- 📘 on proration: policy choices spelled out (day-count conventions).
- 🔍 on the RED commit: "read a bug fix commit-first: the test is the spec of the bug."
- ⚠️ on carryover cap application order (accrue-then-cap vs cap-then-accrue) — tested both ways.
- 🔗 back to S03 commit that planted it (link the sha) — honest post-mortem tone.

## Debate

`[Reviewer]`: "Balance as a stored column is an invitation to drift; derive it from an event
stream of accruals/spends." `[Author]`: agreed on the ledger, not on the timing — we store the
accrual *ledger rows* (auditable) AND a materialized balance, reconciled by a test. Full event
sourcing is more machinery than this product's questions need. ADR-0007 records the middle path.

## Exercises

- Add a "use it or lose it" December 31 forfeiture rule as policy data. What tests does it demand?
- Break the reconciliation invariant on purpose (skip a ledger row); watch which test catches it.
- Why is `DateOnly.FromDateTime(clock.UtcNow.Date)` still wrong for a worker in Manila? Fix it.
