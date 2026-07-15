# ADR-0006: Hiring as an Explicit State Machine + Named Transition Endpoints

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 05)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

A hire moves through stages — `quote → offer → contract → onboarding → active`, with an early
`withdrawn` exit — and each move has rules (you cannot activate a hire nobody accepted; you cannot
withdraw once onboarding has begun). This is the product's first real *workflow*, and Phase 1 ends
here (`v0.5.0`). How we model it sets the pattern for the payroll cycle machine (S7) and every
approval flow after it.

Two questions had to be settled: where the transition rules live, and how the API exposes them.

## Decision 1: transitions are DATA + guard methods, not a status column

`EngagementState` is a plain enum. The *rules* — which `(from → to)` moves are legal — live in one
pure table, `EngagementTransitions`, behind `IsLegal(from, to)`. The `Engagement` aggregate exposes
a method per transition (`SendOffer`, `AcceptOffer`, `CompleteOnboardingItem`, `Activate`,
`Withdraw`); each asks the table whether its move is legal from the current state and throws a
precise `DomainRuleException` (→ 422) if not. There is no public state setter.

- **Making illegal states unrepresentable beats validating them.** The only way to reach `Active`
  is through every guard along the way; there is no code path that sets `State = Active` directly,
  so "activated without onboarding" is not a bug we test for — it is a sentence we cannot write.
- **The illegal half is the valuable half.** The matrix test asserts all 6×6 pairs. The six legal
  ones are obvious; the thirty illegal ones (skip-the-offer, resurrect-a-withdrawn-hire,
  go-backwards) are exactly what a `switch (status)` with scattered `if`s forgets — because nobody
  writes the moves they *don't* intend, and nobody tests them.
- **`Contract` vs `Onboarding` is a real boundary, not bookkeeping.** Withdrawal is legal from
  `Offer` and `Contract` but not once onboarding starts (the first completed checklist item). The
  distinct state is what makes that rule expressible in the table instead of as a special case.

## Decision 2: state changes with business meaning get NAMED endpoints

Transitions are `POST /engagements/{id}/send-offer`, `/accept-offer`,
`/checklist/{item}/complete`, `/activate`, `/withdraw` — not `PATCH /engagements/{id}` with a
`{ "status": "..." }` body.

The Reviewer argued the named endpoints are "RPC in REST clothing" and that `PATCH`-to-status is
the RESTful move. The debate (recorded on the PR) landed on named endpoints, and this ADR records
the rule:

> **A state change that carries business meaning — its own payload, its own guard, its own audit
> verb — is a named endpoint. A field edit is a `PATCH`.**

Reasons the rule came out this way:

- **Per-transition contracts.** `send-offer` needs a candidate; `accept-offer` needs a date;
  `activate` needs nothing. A single `PATCH` body has to make every field optional and validate
  the combinations by hand — the named endpoints *are* the per-transition schema, and OpenAPI
  documents each one.
- **Guards become 422s with meaning, not a 409 of mystery.** `PATCH status=active` on a hire
  mid-onboarding is one generic conflict; `POST /activate` returns "onboarding is not complete
  (2 of 4 items done)".
- **The verbs are the audit log.** Each transition writes `engagement.offer_sent`,
  `engagement.activated`, … — the timeline reads itself. A status `PATCH` would need to *infer* the
  verb from the diff.

This does not license RPC everywhere: field edits (rename an org) stay `PATCH`. The test is
"business meaning," and it is a judgement the ADR now anchors.

## Decision 3: the notification seam is a port, logged for now

Every transition also publishes a typed `AtlasEvent` through `IEventSink`. The only adapter today
is `LoggingEventSink` (an info line). This names the "events leave the system" seam while it is
trivial; S10 replaces the adapter with a transactional outbox and nothing in the use cases changes.
Activation additionally creates the Worker + initial `EmploymentRecord` (S3 flow) in the **same
commit** as the state change and audit row — one transaction, no half-hires.

## Consequences

- Adding a state or a transition is a one-line edit to `EngagementTransitions` plus the matrix test
  turning red until it agrees — the shape of the machine is a reviewable diff (and a count: "there
  are exactly six legal transitions").
- The pattern is reusable: S7's pay-cycle machine (`draft → review → approved → executed`) and the
  S8 approval flow follow this shape — table + guarded methods + named endpoints.
- The engagement module reads a quote only through a narrow `IQuoteReader` port (an anti-corruption
  seam adapted in Infrastructure), so it is decoupled from the S4 Quote aggregate's internals.
- The `IEventSink` ordering relative to commit is a Phase-2 concern (S10 outbox), flagged here as
  the seam it is.

## Rejected alternatives

- **Status enum + `if`s at call sites.** Encodes the same rules as behaviour spread across use
  cases, where the illegal moves are invisible and untested. The table centralizes the rule and
  makes the illegal half assertable.
- **`PATCH /engagements/{id}` with a target status.** Loses per-transition payloads and turns every
  guard into a generic 409; see Decision 2.
- **A workflow-engine library (state-machine framework / BPM).** Enormous dependency for six states
  and six edges; hides the very logic this sprint exists to teach. Revisit only if a customer needs
  user-defined workflows — not on the roadmap.
- **A single `Transition(to)` public method on the aggregate.** Would push per-transition data
  (candidate, accepted-date) into a bag of optional args and re-scatter the "which fields for which
  move" logic the named methods keep tidy.
