# Date Math Is Business Logic (Not Utility Code)

**One-pager · Sprint 06 · pairs with ADR-0007**

The moment code asks "how many days?", "is it still within the window?", or "what rolls over into
next year?", it has left the land of utilities and entered the land of *policy*. Every one of
those questions has several defensible answers, and the difference between them is money and
compliance. This page is the written-down set of answers Atlas chose.

## The clock is an input, not a fact

A function that reads the clock has a hidden timezone and a hidden "now" — two inputs nobody chose
and nobody can control in a test. Atlas's rule (enforced by `AmbientTimeBanTests`): the domain
never reads a clock. Time arrives as a `TimeProvider`-derived value at the edge, or as a
`DateOnly` / `DateTimeOffset` **parameter**.

The bug that forged this rule: probation-end read the server's *local* clock, so a Manila worker's
probation flipped a day early on a US-hosted server. `DateTime.Now` is not a convenience — it is a
dependency on the deployment's timezone, smuggled in without a constructor.

## Two DST myths, defused

- **"Add a month" is not "add 30 days."** Statutory windows are defined in *months*: six months
  from January 31 is July 31, not "January 31 + 182 days". Atlas uses `DateOnly.AddMonths`, which
  clamps correctly (Jan 31 + 3 months → Apr 30, because Apr 31 does not exist).
- **DST does not exist in leave math.** Leave is counted in **calendar days** (`DateOnly`), which
  have no time-of-day and therefore no 23- or 25-hour days to worry about. Storing a leave date as
  a `DateTimeOffset` would invent a fictional time-of-day and re-introduce the very timezone bug we
  just removed. Business time is `DateOnly`; system time (when the row was written) is
  `DateTimeOffset` UTC. Keep them apart (see `temporal-data.md`).

## The accrual choices Atlas made

| Question | The default that does NOT exist | Atlas's chosen answer |
|---|---|---|
| When does a month's leave land? | — | At the **start** of the month (ask on the 5th, get the whole month) |
| Which months are prorated? | — | Only the **hire month** |
| Proration day-count? | 30-day month fiction | **Actual** days (16th of a 31-day month → 16/31) |
| Carryover at year end? | — | **Cap-then-accrue**: forfeit stale days *before* the new year earns |
| Below the country's legal minimum? | silently allow | **Refuse** at construction, naming the minimum |

None of these is "obviously right". They are *chosen*, and a different product could choose
differently — which is exactly why they are written here and pinned by `LeaveAccrualTests`, not
left implicit in a helper.

### Cap-then-accrue vs accrue-then-cap

At a leave-year boundary you must decide which happens first. Say a worker has 12 days and the cap
is 5:

- **Cap-then-accrue** (Atlas): 12 → forfeit to 5 → then earn the new year on top. A fresh year
  always has room for its full entitlement.
- **Accrue-then-cap**: earn the new year first, *then* clip the total to 5 — last year's surplus
  eats this year's earnings. Stingier, and rarely what a policy means.

Both are tested. The test names the choice so a reviewer sees it; the comment in `LeaveAccrual`
explains why. That is the whole discipline: a policy fork is a decision, and a decision gets a test
and a sentence.

## Working days, honestly scoped

`LeaveRequest.WorkingDays` counts weekdays in `[From, To]`. It does **not** know public holidays —
those are a country-and-region reference-data problem of their own, explicitly out of Sprint 06's
scope. The naïveté is labelled, not hidden: a comment on the property, a note in the ADR. A known
gap you can see beats a hidden approximation you trip over.

## The senior habit

When you see date arithmetic in a diff, ask three questions: *Whose clock? Which day-count?
What happens at the boundary?* If the code answers none of them, it has silently chosen — and
silent choices in date math are how "what did we owe them in March?" becomes unanswerable.

## Try it

1. Add a "use it or lose it" December 31 forfeiture as policy data (cap = 0). Which existing test
   already covers it, and which new edge does it demand?
2. Break the accrual on purpose: switch `cap-then-accrue` to `accrue-then-cap` in `LeaveAccrual`.
   Which table row in `LeaveAccrualTests` turns red, and is the new number more or less generous?
3. Explain why `DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)` is *still* wrong for a worker
   in Manila. Then sketch the fix (hint: it needs a field Atlas does not store yet).
