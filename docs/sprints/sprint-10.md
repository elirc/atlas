# Sprint 10 — The Integration Surface (Integration Flagship)

**Goal:** other software can now integrate with Atlas honestly: webhooks that don't lie,
retries that don't double-pay, partners with scoped keys. **Harvests: ledger #3 (double-pay) and
ledger #4 (phantom events).**

## Scope

**In:** **Transactional outbox** — domain events written to `OutboxMessage` in the same
transaction as state; background dispatcher (hosted service) delivers with exponential backoff;
**webhook subscriptions** (per-org endpoint + secret, event-type filter) with **HMAC-SHA256
signed** deliveries (`X-Atlas-Signature: t=...,v1=...`), timestamp tolerance, delivery log +
redelivery endpoint; **Idempotency-Key middleware** for money-moving POSTs (key + request-hash
stored; replay returns original response; mismatched body on same key → 422); **partner API
keys** (hashed at rest, scoped: `read:workers`, `write:invoices`, ...); `docs/http/webhooks.http`.

**Out:** inbound webhooks from vendors, event catalogs/schemas registry (one-pager mentions).

## Teaching focus

At-least-once delivery + idempotent consumers as THE distributed-systems contract. Why
"publish after commit" and "publish in the transaction" are both wrong, and the outbox is the
honest third option.

## The harvest arcs — in-PR commits

**Ledger #4 (phantom events, planted S5):**
1. RED test: transition use case with failing SaveChanges still publishes → observed by fake sink.
2. RED test #2: crash-after-commit loses the event entirely (no durable record).
3. Fix: events go to outbox rows in the transaction; dispatcher delivers post-commit. Both REDs green.

**Ledger #3 (double-pay, planted S9):**
4. RED test: pay-invoice called twice (client retry) → `IPaymentProvider.Pay` invoked twice.
5. Fix: Idempotency-Key middleware + use-case natural-key guard; replay returns original result.

## Commit narrative (sketch)

1–5. The two harvest arcs above (each RED and fix as separate commits)
6. `feat(application+infrastructure): webhook subscriptions + HMAC delivery + dispatcher`
7. `feat(api): partner API keys with scopes`
8. `docs: ADR-0011 the outbox, ADR-0012 idempotency middleware, concepts/at-least-once.md`

## Teaching placements

- 📘 on the outbox table: the two wrong options and why they're wrong (diagrams in one-pager).
- 📘 on HMAC signatures: constant-time compare, timestamp tolerance, replay windows.
- 🔍 on the dispatcher: "read a background worker for its failure modes first: poison messages,
  ordering, shutdown."
- ⚠️ on idempotency storage: key alone is not enough — hash the body (same key, different body
  is a client bug to surface, not mask).
- 🔗 both harvests link the planting shas (S5/S9) — the blind-spot comments now get named.

## Debate

`[Reviewer]`: "A hosted-service dispatcher inside the API process is a toy; use a real queue."
`[Author]`: a queue moves the at-least-once problem, it doesn't solve it — the outbox is the
part that matters and it survives a later move to any queue (the port is the outbox table
itself). In-process polling is honest for our scale and testable in CI. ADR-0011 records the
migration trigger (delivery volume / multi-node). Reviewer: "agreed, and the runbook (S15) must
cover dispatcher lag."

## Exercises

- Subscribe a webhook, break its endpoint, watch backoff in the delivery log; then redeliver.
- Forge a webhook body and try to make the signature pass. Explain each defense you hit.
- Add `Idempotency-Key` to payroll's execute endpoint (S7 built its own natural-key guard —
  do both layers agree or fight?).
