# Runbook: Webhook Replay / Redelivery

**Signal:** a partner reports missing events, or the delivery log shows `Failed` rows. **Severity:**
SEV3 (one partner) unless a broad outage means many partners missed money-relevant events (SEV2).

## Mental model (ADR-0011)

- An event becomes an `OutboxMessage` in the same transaction as the state change. The dispatcher
  delivers it **at-least-once** to every matching subscription via `WebhookEventDelivery`.
- Each attempt records a per-subscription `WebhookDelivery` row: `Delivered` or `Failed`, with an
  attempt count. A partner's failure does **not** throw — it is recorded and left for redelivery, so
  one broken endpoint never triggers a duplicate storm to the endpoints that already succeeded.
- Deliveries are HMAC-signed (`X-Atlas-Signature: t=...,v1=...`) over `"{t}.{body}"`. A replay carries
  the same signed body; the timestamp inside the signature lets the partner detect an old replay.

## Diagnose

```http
### List a subscription's deliveries; look for Status=Failed and the attempt count.
GET {{host}}/api/v1/webhooks/deliveries?subscriptionId={{subscriptionId}}
Authorization: Bearer {{orgAdminToken}}
```

- **All Failed, recent:** the partner endpoint is down or slow. Confirm with them before replaying —
  replaying into a still-broken endpoint just re-fails and burns attempts.
- **Some Failed, old:** a transient blip already backed off. These may have since delivered on their
  own; check for a later `Delivered` row for the same event before acting.

## Redeliver

```http
### Redeliver one recorded delivery. The stored payload is re-sent and re-signed.
POST {{host}}/api/v1/webhooks/deliveries/{{deliveryId}}/redeliver
Authorization: Bearer {{orgAdminToken}}
```

- The response is the updated `WebhookDelivery` (`Delivered`, incremented attempts).
- Idempotency is the partner's job: the event's stable subject + timestamp let them dedupe a
  redelivery of something they already processed. If a partner double-processes a redelivered
  `invoice.paid`, that is their missing idempotency — surface it, do not work around it by withholding
  redeliveries.

## Bulk replay after a partner outage

There is no bulk-replay endpoint by design (S10 deferred per-endpoint auto-redrive). For a handful of
failed rows, redeliver each by id. For a large outage, the honest options are:

1. Wait for the partner to confirm recovery, then script the per-id redelivery over the `Failed` rows
   for that subscription (read the delivery list, POST redeliver for each id). Rate-limit yourself so
   you do not become the retry storm.
2. If the events are still **undispatched at the outbox layer** (not yet delivered to anyone), the
   dispatcher will deliver them at-least-once once the endpoint recovers — nothing to do but confirm
   the outbox is draining (`outbox-dispatcher-lag.md`).

## What NOT to do

- Do not re-emit the business event to "resend" a webhook. That writes a **new** outbox row and a new
  event id, and now the partner cannot tell it apart from a genuinely new occurrence. Redeliver the
  recorded delivery instead — same event, same signature semantics.
- Do not disable signature verification to "help" a partner debug. The signature is the only thing
  standing between them and a forged `invoice.paid`.

## Follow-up

- If a subscription fails often, the recorded deferral (ADR-0011) is per-endpoint auto-backoff/redrive
  — a second drain over `WebhookDelivery`. Worth building if manual redelivery becomes routine.
