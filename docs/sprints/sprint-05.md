# Sprint 05 — Hiring Lifecycle State Machine → v0.5.0

**Goal:** the hiring journey becomes an explicit, guarded state machine:
`quote → offer → contract → onboarding → active` — and the MVP ships.

## Scope

**In:** Engagement aggregate wrapping the lifecycle; transitions with guards (offer needs a quote
+ candidate details; contract needs offer acceptance; active needs onboarding checklist complete);
onboarding checklist (country-driven item list, v1); ActivityLog entries per transition;
engagement endpoints (`POST /engagements`, transition endpoints, timeline view); notification
stub (`IEventSink` port, logging fake) for "engagement activated"; `docs/http/engagements.http`;
**tag v0.5.0 + GitHub Release after merge**.

**Out:** compliance documents (S11), webhooks (S10 — the stub is the seam).

## Teaching focus

State machines: transitions as methods with guards, not status-column writes. Making illegal
states unrepresentable vs validating them. The activity timeline as the product's memory.

## Commit narrative (sketch)

1. `feat(domain): EngagementState + transition table (pure)`
2. `test(domain): exhaustive transition matrix — legal and illegal`
3. `feat(domain): Engagement aggregate + onboarding checklist`
4. `feat(application): transition use cases + activity log + IEventSink`  ← **ledger #4 planted here**
5. `feat(infrastructure+api): persistence, endpoints, timeline`
6. `docs: ADR-0006 state machines, concepts/state-machines.md; release notes`

## Teaching placements

- 📘 on the transition table: state machines as data + guard methods; compare with status-enum-and-ifs.
- 🔍 on the transition matrix test: "the illegal half of the matrix is the valuable half."
- ⚠️ on checklist completion: idempotent transition calls (retrying 'activate' must not duplicate
  activity rows) — *this comment is honest and correct*, and makes the nearby silent flaw crueler.
- 🔗 IEventSink ← S10 foreshadow: "today a log line; one day, someone else's system."
- **Ledger #4:** the use case calls `IEventSink.Publish` *before* `SaveChanges`. A `[Reviewer]`
  comment on the same file praises the port abstraction. No comment on ordering.

## Debate

`[Reviewer]`: "Transition endpoints (`POST /engagements/{id}/send-offer`) are RPC in REST
clothing; do `PATCH` with a target status." `[Author]`: PATCH-to-status turns every guard into a
409-of-mystery and loses per-transition contracts (offer needs a payload; activate doesn't).
Transitions are the resource's verbs — model them. Reviewer accepts; ADR-0006 records the rule:
*state changes with business meaning get named endpoints*.

## Post-merge

Tag `v0.5.0`, GitHub Release: MVP recap (S1–S5 arc), what Phase 2 adds, study order refresher.

## Exercises

- Add a `withdrawn` terminal state reachable from offer/contract only. How many files? Why so few?
- Write the test proving a rejected transition leaves no activity row (then check: does it pass?).
- Sketch the onboarding checklist for a country with a works-council requirement — data or code?
