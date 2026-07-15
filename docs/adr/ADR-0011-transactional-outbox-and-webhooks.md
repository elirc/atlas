# ADR-0011: The Transactional Outbox and Signed Webhooks

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 10)
- **Deciders:** [Author], [Reviewer]

## Context

Since S05 every meaningful state change has called `IEventSink.PublishAsync(...)` right before
`IUnitOfWork.CommitAsync(...)`. With the only sink being a log line that fired immediately, that
ordering was a latent bug: **publish-before-commit**. A transaction that rolled back had already
announced an event that never happened (a *phantom*); and the naive fix — move the publish to
*after* commit — trades the phantom for a *lost* event when the process dies in the gap between
commit and publish. Neither is acceptable once those events drive partner webhooks that move real
integrations.

## Decision 1: a transactional outbox

`IEventSink`'s adapter becomes `OutboxEventSink`, which does not deliver anything — it **enqueues an
`OutboxMessage` row through the use case's own `AtlasDbContext`**. Because the row is written in the
same change tracker that `CommitAsync` saves, the event and the state change commit or roll back
**atomically**. A separate dispatcher (`OutboxDrainService`, a hosted `BackgroundService` driving
`OutboxRelay`) reads committed rows and delivers them through `IEventDelivery` adapters, marking each
dispatched only after every delivery succeeds.

The twenty call sites did **not** change. The publish-before-commit ordering that used to be the bug
is now correct, because "publish" means "stage a row in this transaction." We changed what the seam
*does*, not who calls it — the payoff of having named the port in S05.

- **Rejected: publish after commit.** Loses events on a crash in the commit→publish gap. There is no
  ordering of two separate systems (DB, broker) that is safe without a durable record bridging them;
  the outbox row *is* that record.
- **Rejected: publish inside the transaction to a broker.** A broker enlisted in the DB transaction
  is a distributed transaction (2PC) — the operational complexity we are specifically avoiding, and
  most brokers can't honestly participate anyway.

## Decision 2: at-least-once, and therefore idempotent consumers

The dispatcher delivers, **then** marks dispatched. A crash in between redelivers on the next drain.
This is **at-least-once**: we would rather deliver twice than zero times. Exactly-once delivery is a
distributed-systems fairy tale; the honest contract is at-least-once delivery **plus idempotent
consumers**. That is why S10 also ships the `Idempotency-Key` middleware (ADR-0012) — Atlas must be a
well-behaved *receiver* of its own retries, and it asks the same of its partners.

## Decision 3: an in-process dispatcher, not "a real queue"

The dispatcher is a hosted service polling the outbox table inside the API process.

- **The Reviewer's objection:** "a hosted-service dispatcher is a toy; use a real queue."
- **Resolution:** a queue moves the at-least-once problem, it does not remove it — and the part that
  matters, the *durability*, is the outbox table, which survives any later move to a broker unchanged
  (the port is the table). In-process polling is honest for our scale and trivially testable in CI
  (drive `OutboxRelay.DrainOnceAsync` directly). **Migration trigger, recorded:** move to a broker
  (or `LISTEN/NOTIFY`-driven drain) when delivery volume or multi-node deployment makes polling
  latency or duplicate-drain contention real. The S15 runbook must cover **dispatcher lag** as an
  operational signal.

Failure modes are named in `OutboxRelay`: poison messages are capped at `MaxAttempts` then parked
(not looped); ordering is best-effort by `OccurredAtUtc`, never a guarantee consumers may lean on;
each drain is batch-bounded so a backlog can't allocate the world.

## Decision 4: webhooks are HMAC-signed, and the fan-out does not throw

Partner deliveries carry `X-Atlas-Signature: t=<unix>,v1=<hmac-sha256>` over `"{t}.{body}"`, keyed
by the subscription's secret. The timestamp is inside the signed content so a receiver can reject
replays; verification uses a constant-time compare. `WebhookEventDelivery` records a per-subscription
`WebhookDelivery` row (Delivered/Failed) and **does not throw** on a partner's failure — throwing
would make the outbox retry the whole event and re-POST to the endpoints that already succeeded
(duplicate storms from one broken endpoint). Failed rows are redelivered on demand.

- **Recorded deferral:** automatic per-endpoint backoff/redrive is out of scope for S10; today a
  failed delivery is redelivered via the API. The outbox already demonstrates automated at-least-once
  at the event layer; per-subscription auto-retry is a straightforward follow-up (a second drain over
  `WebhookDelivery`).
- **Recorded debt:** the signing secret is stored in readable form because we must sign with it. A
  leaked table lets an attacker forge deliveries. Production would envelope-encrypt these (KMS); the
  course keeps plaintext and names the exposure. Added to the S13 threat-model backlog.

## Consequences

- Events are exactly as durable as the state that produced them — no phantoms, no losses.
- Consumers (ours and partners') must dedupe; the event carries a stable subject + timestamp to make
  that possible.
- The outbox table is a new operational surface: its depth and oldest-undispatched age are the health
  signals the S15 runbook watches.
