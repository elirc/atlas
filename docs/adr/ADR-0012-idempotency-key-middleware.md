# ADR-0012: The Idempotency-Key Middleware

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 10)
- **Deciders:** [Author], [Reviewer]

## Context

A client that POSTs `/invoices/{id}/pay`, times out waiting for the response, and retries has, from
its side, made **one** request. Without help, the server sees two, and — in the window before the
first commit lands — can move money twice. This is ledger flaw #3, planted in S09: `PayInvoice`
guarded only on `invoice.Status`, which does not close the concurrent-retry / crash-before-commit
window. The outbox (ADR-0011) makes Atlas deliver events at-least-once; this ADR makes Atlas *receive*
requests idempotently — the other half of the same contract.

## Decision 1: an `Idempotency-Key` header, mandatory on money-moving POSTs

The client sends `Idempotency-Key: <unique>`. A middleware, running after auth (so keys scope per
org) and inside the exception handler, stores the response of the first request under `(org, key)` and
**replays** it for any repeat. On money-moving routes (`/pay`, `/execute`) the header is **required**
— a 400 if absent — because a money-moving request without retry-safety is not one we will execute.
Elsewhere the header is honored if present.

## Decision 2: the request FINGERPRINT is load-bearing, not just the key

The stored record holds a SHA-256 of `method + path + body`. A repeat with the **same** key but a
**different** fingerprint is a client bug (a key accidentally reused for a new request) and returns a
**422 `idempotency-conflict`** — its own problem type, distinct from a business-rule 422, so a client
can tell "retry with a fresh key" from "fix the request." Replaying the first response for a genuinely
different request would be silent, dangerous nonsense; we surface it instead.

- **Rejected: key-only storage.** Masks the reused-key bug and can replay the wrong response.
- **Rejected: dedupe only inside the use case** (a natural-key guard). Payroll's `ExecutePayrollRun`
  *does* have one (S07), and it still stands — but a use-case guard cannot replay the original
  response bytes to a retrying client, and does not generalize to every money-moving endpoint. The
  two layers are defense in depth: the middleware dedupes identical retries at the edge; the use case
  makes a genuine re-entry a no-op. They agree rather than fight (see PayrollEndpointsTests).

## Decision 3: only successful (2xx) responses are stored

A 4xx/5xx left nothing worth being idempotent about, and a 503 (vendor unreachable) **must** stay
retryable — storing it would pin a transient failure as the permanent answer to that key. Storing
only 2xx also keeps the store safe: after a committed 2xx the request's change tracker is clean, so
recording the idempotency row (through its own short-lived `DbContext`) can never accidentally flush a
half-finished use case.

## Decision 4: precedence — authenticate before demanding a key

The mandatory-key 400 is enforced only for authenticated callers. An anonymous money-moving request
flows to the endpoint and gets a 401: "who are you" precedes "where is your idempotency key." Keys are
org-scoped, so there is no scope to enforce before we know the org anyway.

## Consequences

- A retrying client gets the original response replayed (`Idempotency-Replayed: true`), not a second
  charge and not a confusing "already paid" 422.
- Concurrency: the unique `(org, key)` index turns a simultaneous double-submit into a collision the
  loser reads back, not a double-execution.
- The store is a new table with a natural TTL story (old keys can be swept); retention is an ops knob,
  not a correctness one.

## Notes / recorded bets

- **Storage is a port** (`IIdempotencyStore`): a table today, Redis-with-TTL tomorrow, without the
  middleware knowing.
- **Cross-node** correctness rides on the unique index in the shared database; it does not depend on
  the single-process assumption the outbox dispatcher currently makes.
