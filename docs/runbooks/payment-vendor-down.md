# Runbook: Payment Vendor Is Down (503 storm)

**Signal:** `/api/v1/invoices/{id}/pay` is returning `503 vendor-unavailable`, contractors are not
being paid, the payment vendor's status page is red. **Severity:** SEV2 (payments are a core money
flow); **SEV1 if money is moving but not being recorded** — that is a different, worse incident (see
"When 503 is NOT the real problem").

## Why a 503 here is the honest answer

`PayInvoice` reaches the vendor through `IPaymentProvider` — the anti-corruption layer (ADR-0010).
When the adapter cannot reach the vendor, it returns `PaymentResult.VendorUnavailable`, which the use
case turns into `PaymentProviderUnavailableException` → **503 `vendor-unavailable`**, and crucially
**does not write a failure**. "We couldn't reach the vendor" is not "the payment failed" — recording
it as failure would lie to reconciliation. The invoice stays `Approved` (still payable), and the
client is told to retry (ADR-0012: a 503 is never stored by the idempotency middleware, so it stays
retryable).

So during a vendor outage, the correct state is: no money moved, no failures recorded, invoices
still payable, clients retrying. Your job is to **not make it worse**, not to force payments through.

## Triage

1. Confirm it is the vendor, not us. `/health/ready` should be `ready` (this is not our outage). The
   logs show `PaymentProviderUnavailableException` with the adapter's reason, not a 500.
2. Check whether **any** payments are succeeding (partial outage) or none (full). The vendor status
   page + a single canary pay attempt on a low-value invoice tells you.
3. Communicate: tell affected orgs that payouts are delayed by a vendor outage and will be retried;
   there is nothing they must re-submit.

## Do

- **Let clients retry.** The 503 contract exists for this. Their retries carry an `Idempotency-Key`
  (mandatory on `/pay`), so a retry that lands right as the vendor recovers cannot double-pay — the
  key replays the first committed response.
- **Pause automated retriers if they are hammering.** If an internal scheduler is retrying `/pay` in a
  tight loop, back it off — you do not want to DDoS the vendor's recovery. Manual/again-later is fine.
- **Consider the second vendor (S12).** If the affected org has a `VendorAssignment` and the outage is
  vendor-specific, an effective-dated switch to the alternate calculation/payment path may be
  appropriate — but a **cycle in flight finishes on the vendor it started with** (ADR-0014). Do not
  flip a vendor mid-run to dodge an outage; that is how you get divergent payslips. Flip at the next
  cycle boundary if at all.

## Do NOT

- Do not mark invoices `Failed` to "clear the queue". They did not fail; they were never attempted.
  Marking them failed and re-approving later muddies the audit trail and can double-pay on recovery.
- Do not disable the idempotency requirement to "simplify" manual payments during the incident. That
  requirement is what makes your own hurried retries safe.
- Do not restore from backup. Nothing was lost; there is no state to recover.

## When 503 is NOT the real problem

If contractors report being **paid twice**, or the vendor shows charges Atlas has no `invoice.paid`
record for, this is not a 503 storm — it is a reconciliation incident (money moved, we did not record
it). Escalate to SEV1, stop automated payments, and reconcile the vendor's ledger against
`contractor_invoices` + the audit log before resuming. This is the failure the double-pay harvest
(ledger #3, S10) and its postmortem (`docs/postmortems/2025-11-14-double-pay-window.md`) are about.

## Recovery

- When the vendor recovers, in-flight retries succeed and `invoice.paid` events flow to the outbox →
  webhooks. Watch the outbox drains normally.
- Note the outage window and any orgs told about delays. If it was customer-visible, write it up.
