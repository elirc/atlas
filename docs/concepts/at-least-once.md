# Concept: At-Least-Once Delivery and Idempotent Consumers

The one-page version of the distributed-systems contract S10 is built on.

## The problem: two systems, one truth

A use case changes state (the database) AND wants the outside world to know (an event). Those are two
systems, and there is no free lunch that updates both atomically. You get to pick your failure:

| Strategy | The failure |
|---|---|
| **Publish, then commit** | Transaction rolls back → you announced an event that never happened. A **phantom**. |
| **Commit, then publish** | Crash in the gap → the change happened but no one was told. A **lost event**. |
| **Commit + publish in one distributed transaction (2PC)** | Operational complexity, and most brokers can't honestly enlist. |

## The honest third option: the transactional outbox

Write the event as a **row in the same transaction** as the state change. Now they are one atomic
write — no phantom (rollback drops the row too) and no loss (the row is as durable as the state). A
separate **dispatcher** turns rows into deliveries later.

```
┌─ use case transaction ─────────────┐
│  UPDATE invoice SET status='Paid'   │   both commit,
│  INSERT outbox_messages (...)       │   or neither does
└─────────────────────────────────────┘
                 │  (later, separate)
                 ▼
        dispatcher: read row → deliver → mark dispatched
```

## The catch: delivery can still double

The dispatcher delivers, then marks the row dispatched. Crash in *that* gap → it redelivers next time.
So the outbox gives you **at-least-once**, never exactly-once:

> **Exactly-once delivery does not exist.** What exists is at-least-once delivery **+ idempotent
> consumers**, which together behave as if exactly-once — this is exactly-once *processing*.

You cannot make the network promise to deliver a message once. You *can* make the receiver treat a
duplicate as a no-op. That is where the burden honestly lives: on the consumer.

## Being a good consumer: idempotency keys

Atlas is a consumer of its own clients' retries. A client that times out and resends a payment has, to
itself, made one request. The `Idempotency-Key` header lets Atlas recognize the retry and **replay the
first response** instead of paying again:

- **Key** scopes the "same request" claim (per org).
- **Request fingerprint** (hash of method+path+body) catches a key reused for a *different* request —
  a bug to surface (422), not to mask.
- **Store only success** — a failed call left nothing to be idempotent about, and a transient 503 must
  stay retryable.

And Atlas asks the same of its partners: every webhook carries a stable subject + timestamp so a
partner can dedupe a redelivered event.

## Two layers that agree, not fight

Payroll's `ExecutePayrollRun` already had a **natural-key guard** (S07): re-executing an executed run
is a domain no-op. S10 adds the **middleware** on top. They are defense in depth:

- The middleware dedupes an *identical retry* at the edge (same key → replay the bytes).
- The use case makes a *genuine re-entry* (different key, same run) a no-op.

Test them together and they agree — the middleware never has to know the use case is also careful, and
the use case never has to know about HTTP.

## The rules, condensed

1. Never publish outside the transaction that produced the event.
2. Assume every delivery can arrive twice; design consumers to not care.
3. An idempotency key without a request fingerprint is a footgun.
4. "Exactly-once" in a design doc is a smell; look for the idempotent consumer that makes it true.
