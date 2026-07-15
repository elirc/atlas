# Runbook: Rotate a Partner API Key or Webhook Secret

**When:** routine rotation, a suspected leak, or offboarding a partner. **Severity:** SEV3 for
routine; **SEV1 if a secret is known-leaked** (an attacker can forge webhook deliveries or call the
API as the partner until you rotate).

## Two different secrets, two different blast radii

| Secret | Where it lives | If leaked, an attacker can... | Rotation |
|---|---|---|---|
| **Partner API key** (`atk_...`) | hashed at rest (S10); shown once at creation | call the API within the key's scopes, as that org | create new, cut over, revoke old |
| **Webhook signing secret** (`whsec_...`) | stored readable (must sign with it — recorded debt, ADR-0011) | **forge** deliveries the partner will trust | re-register (new secret), partner updates verifier |

## Rotate an API key

API keys are scoped and hashed; you cannot recover the plaintext, only mint a new one.

```http
### 1. Create the replacement with the SAME scopes as the old key. The plaintext is shown ONCE.
POST {{host}}/api/v1/api-keys
Authorization: Bearer {{orgAdminToken}}
Content-Type: application/json

{ "name": "partner-x-rotated-2026-07", "scopes": ["read:workers", "write:invoices"] }

### 2. Give the new key to the partner over a secure channel. They cut over.

### 3. Confirm the new key works (a read within scope), then REVOKE the old one.
DELETE {{host}}/api/v1/api-keys/{{oldKeyId}}
Authorization: Bearer {{orgAdminToken}}
```

- A revoked key stops authenticating immediately (it fails the `IsActive` check → 401). There is no
  grace window — so cut over **before** revoking, not after.
- If the key is **known-leaked**, invert the order: revoke first (accept the partner's brief outage),
  then mint and hand over the replacement. A short outage beats an open door.
- Scope hygiene: rotation is a good moment to narrow scopes. If the partner never writes invoices,
  the new key should not carry `write:invoices`.

## Rotate a webhook signing secret

The secret is per subscription and is revealed **once** at registration. There is no "reveal again"
by design (the list view has no secret field — the type makes leakage impossible). So rotation = a new
subscription, coordinated with the partner's verifier.

1. **Register a new subscription** to the same URL + event filter. Capture the new `whsec_...` once.
   ```http
   POST {{host}}/api/v1/webhooks
   Authorization: Bearer {{orgAdminToken}}
   Content-Type: application/json

   { "url": "https://partner.example/hooks", "eventTypes": null }
   ```
2. **Partner adds the new secret** to their verifier, accepting signatures from *either* secret during
   the overlap (both subscriptions deliver; they dedupe on event subject + timestamp).
3. **Pause the old subscription**, watch that the new one delivers cleanly, then remove the old.
   ```http
   POST {{host}}/api/v1/webhooks/{{oldSubscriptionId}}/pause
   Authorization: Bearer {{orgAdminToken}}
   ```
4. If the secret is **known-leaked**: pause the old subscription immediately (stop signing with the
   burned secret), then stand up the new one. The partner gets a brief gap rather than a window in
   which forged deliveries validate.

## After any rotation

- Confirm no deliveries are failing on the new subscription (`webhook-replay.md` to check the log).
- Record the rotation (who, which secret, why) in the audit trail — rotations are exactly the events a
  future incident timeline needs.
- Recorded debt to remember: webhook secrets are stored readable because we must sign with them; a
  leaked `webhook_subscriptions` table is a forge-everything event. Production envelope-encrypts these
  (KMS). Named in ADR-0011 and the threat model — rotation is the mitigation until then.
