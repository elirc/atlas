# Postmortem: A Contractor Was Paid Twice (the Double-Pay Window)

- **Date of incident:** 2026-07-15 (pilot/early-access org, during the S10 integration build)
- **Authors:** [Author] (IC), [Reviewer]
- **Status:** Resolved; fix shipped in S10 (PR #61); regression test pins it
- **Severity:** SEV1 (money moved twice)
- **Ledger:** this is the incident **ledger flaw #3** would have caused at scale — planted in S09,
  harvested in S10. The full accounting is in `docs/sprints/flaw-ledger.md`.

> This one really moved money (to a pilot org's test contractor, recoverable). We are writing it up
> in full because it is the most valuable kind of incident: a small, contained one that reveals a bug
> which, left alone, becomes a large uncontained one. Blameless — no one "clicked twice"; the system
> made a single client action into two payments.

## Summary

A pilot client's integration POSTed `/api/v1/invoices/{id}/pay`. Our payment vendor (PayGrid, mock)
was briefly slow; the client's HTTP call timed out at *their* gateway after the request had already
reached us and started moving money. The client's framework did what well-behaved clients do — it
**retried**. The retry arrived before the first request had committed, saw the invoice still
`Approved`, and paid the contractor a **second** time. From the client's side, they made one request.

## Impact

- One contractor paid twice on one invoice (pilot environment; funds recovered by reversing the
  duplicate with the vendor).
- Trust cost with the pilot client: "your API double-charged on a retry" is exactly the sentence an
  integration surface exists to never produce.
- Blast radius if it had reached GA: **every** money-moving endpoint under retry, across all orgs.

## Timeline (UTC)

| Time | Event |
|---|---|
| 09:41:02 | Client POSTs `/invoices/{id}/pay`. Request reaches Atlas; `PayInvoice` checks status (`Approved` ✓) and calls `IPaymentProvider.PayAsync`. |
| 09:41:04 | PayGrid is slow to respond (transient). |
| 09:41:05 | **Client gateway times out** and the client's HTTP library auto-retries the same POST. |
| 09:41:05 | The retry reaches Atlas. The first request has **not committed yet** — the invoice is still `Approved`. The status guard passes again. Second `PayAsync` call. |
| 09:41:06 | First request's PayGrid call returns success; `MarkPaid`; `CommitAsync`. |
| 09:41:07 | Second request's PayGrid call also returns success; second payment recorded. |
| 09:41:30 | Reconciliation (manual, during the S10 build) spots two `invoice.paid` for one invoice. |
| Same day | Root cause identified as the status-only guard; the S10 idempotency work is brought forward as the fix. |

## Root cause vs contributing causes

**Root cause:** `PayInvoice` guarded payability **only on `invoice.Status`** (`Approved`/`Failed`). A
status check does not close the concurrent-retry / crash-before-commit window: between the first
request reading `Approved` and its `CommitAsync`, a second identical request reads the same
`Approved` and proceeds. The guard was a check on stale state, not a lock and not a dedupe.

**Contributing causes:**

1. **The retry was correct.** The client did the right thing. A safe API must assume every
   money-moving request can arrive twice — the burden is on the receiver, not the caller.
2. **The provider dedupe was assumed, not guaranteed.** `PaymentInstruction` passed `invoice.Id`, but
   the vendor's dedupe was unreliable and outside our control (ADR-0010 — never trust a vendor to be
   the safety you owe your users).
3. **The neighborhood looked healthy, which hid the gap.** The `PaymentResult` taxonomy is genuinely
   excellent — a closed, exhaustive, compiler-checked result type. Review attention landed there
   ("the whole value of the typed result is right here") and **not** on the missing idempotency two
   lines up. A polite LGTM praised the strong thing next to the weak one. (See the flaw ledger's note
   on blind-spot reviews — this comment is Exhibit A.)

## The fix that shipped (S10)

Two layers that agree rather than fight (concepts/at-least-once.md, ADR-0012):

1. **`Idempotency-Key` middleware**, mandatory on money-moving POSTs (`/pay`, `/execute`). The first
   request's response is stored under `(org, key)` and **replayed** for any repeat — so the retry gets
   the original 200 back, not a second charge. A same-key/different-body request is a client bug we
   surface (422 `idempotency-conflict`), not mask. Only 2xx is stored, so a transient 503 stays
   retryable.
2. **The use-case natural-key guard** already present for payroll (S07), generalized in spirit: a
   genuine re-entry is a domain no-op. The middleware dedupes the identical retry at the edge; the use
   case makes a real re-entry harmless. Tested together, they agree.

**Regression test (RED before the fix):** pay the same invoice twice as a client retry → assert
`IPaymentProvider.PayAsync` is invoked **exactly once**, and the second response is the replayed
original (`Idempotency-Replayed: true`, same `VendorReference`). Delete this test and the window
reopens — which is precisely why a finding without a pinned test regresses.

## Prevention

- **Mandatory** idempotency keys on every money-moving POST — enforced by middleware, not convention.
- **Fingerprint the request** (hash of method+path+body), not just the key, so a reused key on a
  different request is caught, not replayed into the wrong response.
- **Never trust a vendor's dedupe** as your correctness boundary (ADR-0010).
- **Review the neighbors of strong code.** The strongest code on a screen can draw the eye away from a
  gap right beside it. The lesson the flaw ledger exists to teach.

## Lessons

1. A status check is not a lock and not a dedupe. "It's already paid" is not idempotency; it is a race
   with extra steps.
2. Exactly-once delivery does not exist; at-least-once + an idempotent receiver does. Atlas had to
   become the idempotent receiver of its own clients' retries before it could ask partners to be one.
3. The blind spot is next to the brilliant part. Plan reviews accordingly.
