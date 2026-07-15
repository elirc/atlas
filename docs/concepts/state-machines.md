# State Machines: Transitions Are Data

**One-pager · Sprint 05 · pairs with ADR-0006**

Almost every business object has a lifecycle: a hire, an invoice, a payroll run, a support ticket.
The lazy encoding is a `status` column plus `if (status == …)` checks wherever a change happens.
It works on the happy path and rots on every other one. The alternative is small, and it is the
sprint's whole idea.

## The two encodings

| | Status column + `if`s | Transitions as data (Atlas) |
|---|---|---|
| Where the rules live | scattered across use cases | one table: `EngagementTransitions` |
| Illegal moves | implicit (whatever nobody wrote) | explicit (every pair not in the set) |
| Testing the illegal moves | ~never | one exhaustive matrix test |
| Adding a state | hunt every `if` | one row + a red test until it agrees |
| A blocked change | often a silent no-op or a generic 409 | a 422 that names both states |

## The valuable half is the half you don't intend

A six-state machine has 36 ordered pairs. Six are legal. The other **thirty** are the ones that
matter: *quote → active* (skip everything), *withdrawn → active* (raise the dead), *onboarding →
withdrawn* (bail out too late). Nobody writes those moves on purpose, so in the `if`-scattered
design nobody forbids them and nobody tests them — until a retry, a race, or a malicious client
finds one. Atlas asserts all 36:

```
[Theory]
[MemberData(nameof(AllOrderedPairs))]
public void Every_pair_is_legal_exactly_when_the_lifecycle_allows_it(from, to) =>
    Assert.Equal(LifecycleAllows(from, to), EngagementTransitions.IsLegal(from, to));
```

## Make illegal states unrepresentable, don't validate them

There is no `engagement.State = Active` anywhere. `Active` is reachable only by calling
`Activate()`, which asks the table and checks the checklist first. "Activated without onboarding"
isn't a bug we guard against — it's a statement the code cannot express. That is the difference
between *validating* a bad state after the fact and *preventing* its construction.

## Why `Contract` and `Onboarding` are both states

They look redundant until you notice a rule hangs on the seam: **you can withdraw from `Contract`,
but not once onboarding has started.** The first completed checklist item is what "onboarding
started" means, so it advances `Contract → Onboarding` — and withdrawal drops out of the legal set.
A state earns its place when a rule depends on it; delete `Onboarding` and that rule has nowhere to
live.

## The timeline is the product's memory

Each transition stamps a milestone (`OfferSentAtUtc`, `AcceptedOn`, `ActivatedAtUtc`, …) and writes
an audit row with its own verb (`engagement.activated`). Read together, the milestones *are* the
history `GET /engagements/{id}` returns — no separate "history" system, just the states writing
themselves down.

## The senior habit

When a design review shows a `status` string and a switch statement, ask: *what are the moves this
forbids, and where are they tested?* If the answer is "nowhere," you've found the bug that hasn't
happened yet. Draw the six edges; assert the thirty non-edges.

## Try it

1. Add a `paused` state reachable from `onboarding` and back. How many files change? (One for the
   rule, one for the test, one method on the aggregate, one endpoint.) Why so few?
2. Write the test proving a rejected transition leaves no activity row — then run it. Does it pass?
   (It should: the audit write is *after* the guard. Find the line that makes that true.)
3. Sketch the onboarding checklist for a country with a works-council requirement. Is that new item
   data or code? (It's a row in `OnboardingChecklistTemplate` — the S4 rule-pack lesson again.)
