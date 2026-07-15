# Atlas Threat Model

**Sprint 13 · pairs with ADR-0016 and `docs/security/sprint-13-audit.md`**

> Read Atlas like an attacker; write it down like an engineer. This is a living document — a
> map of what Atlas is worth stealing, where the walls are, and where we have *recorded* that a
> wall is a curtain. STRIDE is the checklist we walk the boundaries with, not a religion.

## 📘 [Author] How to use this

Threat modelling is not a security-team ritual you do once and frame. It is the habit of asking, at
every trust boundary, "what does the thing on the other side of this line get to assume, and what
happens when it lies?" The boundaries are where reviews concentrate because the boundaries are where
trust changes hands. Everything below is organised by boundary, then walked through STRIDE
(**S**poofing, **T**ampering, **R**epudiation, **I**nfo disclosure, **D**enial of service,
**E**levation of privilege). A control we have is a wall; a control we have *decided to defer* is a
named curtain with an owner and a trigger. Curtains are honest; forgotten walls are incidents.

## Assets (what is worth stealing)

| Asset | Why an attacker wants it | Blast radius if lost |
|---|---|---|
| Worker PII + pay (salary, terms, bank-ward payment refs) | Fraud, extortion, competitor intel | Per-org, company-ending for that tenant |
| Cross-tenant data | One org reading another's workforce/quotes | **Company-ending for Atlas** (trust product) |
| Session tokens / API keys | Impersonate a user or a partner integration | Per-credential, up to full org |
| Webhook signing secrets | Forge "genuine" events into a partner's systems | Per-subscription, spreads into partner |
| Payroll run execution (money movement) | Pay the wrong account; double-pay | Direct financial loss |
| The audit log | Cover tracks (repudiation) | Loss of forensic truth |

## Trust boundaries (where the walls are)

```
  Internet ─┬─▶ [B1] Bearer auth (session token | API key)  ── ADR-0003, S02/S10
            │
            ├─▶ [B2] Tenancy: use case RequireOrg/RequireRole + org-scoped reads ── ADR-0003
            │
            ├─▶ [B3] Idempotency-Key middleware (money-moving POST replay)  ── ADR-0012
            │
   Atlas ───┼─▶ [B4] Vendor egress: PayGrid payment ACL          ── ADR-0010
            │
            └─▶ [B5] Webhook egress: signed POST to partner URL   ── ADR-0011
                                     ▲ SSRF surface (partner-controlled URL)
   Background: [B6] Outbox dispatcher runs ORG-LESS (no ICurrentContext) ── ADR-0011/0016
```

---

## B1 — Authentication (hand-rolled sessions + partner API keys)

Hand-rolled per ADR-0003 for teaching value; ~300 lines Atlas owns and must audit (this sprint).

| STRIDE | Threat | Control / disposition |
|---|---|---|
| **S** poofing | Steal/guess a token | 256-bit CSPRNG opaque tokens, SHA-256 at rest, 12h lifetime, revocable. API keys hashed at rest, `atk_` prefix. |
| **S** | Brute-force a password online | **NEW (AF-6, this sprint):** login rate-limit + exponential lockout backoff, keyed on the *submitted* identifier so lockout is not an enumeration oracle. |
| **T** ampering | Forge a session by editing a token | Token is opaque + server-side; there is nothing to edit (unlike a JWT). Revocation is server-side by design (ADR-0003 rejects JWTs for this reason). |
| **R** epudiation | "I never did that" | S3 audit log records user id (incl. ops acting in-org) in the same transaction as the act. |
| **I** nfo disclosure | User enumeration via login errors | Uniform "Invalid email or password"; byte-identical failure bodies (AuthFlowTests). Malformed email at login is a failed login, not a 422. Lockout keyed on submitted address preserves this. |
| **D** oS | Password spray / lockout-as-DoS | Backoff is capped (15 min), not permanent. **Curtain (recorded):** an attacker can lock a victim's account by submitting bad passwords; production adds IP-scoped limits + CAPTCHA. Owner: platform. |
| **E** levation | Expired/garbage token treated as anonymous | A *presented* bad credential is **401, never anonymous** (BearerAuthenticationMiddleware). API-key scopes enforced at the edge (`verb:resource`). |

**Recorded debts (ADR-0003):** breach-list password checking (deferred); SSO/OIDC switch trigger
named; PBKDF2 at 600k iterations is the account-at-rest control.

## B2 — Tenancy (the invariant that keeps orgs apart)

The most valuable wall in the product. Owner per layer: `ICurrentContext` (who/org/role) →
`RequireOrg/RequireRole` in use cases → org-scoped repository reads → executable authz matrix.

| STRIDE | Threat | Control / disposition |
|---|---|---|
| **E / I** | **IDOR: read another org's resource by id** | Org-scoped reads (`FindInOrgAsync`) → cross-tenant is **404, not 403** (existence is need-to-know). **AF-3 THIS SPRINT:** `GetWorker` read by bare primary key — fixed; see the audit. |
| **I** | Existence leak via status codes | 404 for cross-tenant, uniform with "not found." |
| **E** | Employee reaches admin data | `RequireRole` next to the logic it guards; worker *detail* is now HR-manager-and-up (AF-3 fix). |
| **T** | Background job writes cross-org | **Curtain [B6]:** the outbox dispatcher runs org-less by design; it must never use the request-scoped context. ADR-0016 documents it as the deliberate exception a global query filter would silently break. |
| **R** | Untraceable admin action | Ops passes role checks but is audited (user id recorded). |

**Standing guardrails:** `AuthzMatrixTests` (persona × endpoint) + `CrossTenantReadAuditTests`
(every by-id use case injects the context port) + per-use-case cross-org characterization tests.
The matrix is **necessary but not sufficient** — it missed AF-3 because the endpoint 404'd via a
paired scoped lookup; the use-case test is what caught it.

## B3 — Idempotency (money-moving replay)

`Idempotency-Key` middleware (ADR-0012) makes paying/executing/billing safe to retry; same key +
different body → 422 idempotency-conflict. Not a security control per se, but the reason a retry
storm (or a replayed request) can't double-pay. **T/D** covered.

## B4 — Vendor egress: PayGrid payment ACL

The anti-corruption layer (ADR-0010) is the only place PayGrid's dialect exists. An **unmapped
vendor state is a loud 500** (pages someone), never a smoothed-over 4xx — because silently mapping
an unknown settlement state to "ok" is how money goes missing quietly. **T/R/I** at this boundary
are the vendor's responsibility inside the ACL; ours is to fail honestly.

## B5 — Webhook egress (SSRF surface + secret handling)

Atlas POSTs signed JSON to a **partner-controlled URL**. Two live concerns:

| STRIDE | Threat | Control / disposition |
|---|---|---|
| **T** | Forge a "genuine" event to a partner | HMAC-SHA256 over `"{t}.{body}"`, timestamp inside the signed content, constant-time verify (ADR-0011). |
| **I** | **SSRF: point a webhook at `169.254.169.254` / `localhost` / RFC-1918** and make Atlas fetch internal metadata | **Belt today:** `WebhookSubscription.Create` is HTTPS-only (defeats plaintext + cheap redirects). **AF-4 (recorded, recommended):** add a registration-time host block (reject loopback / link-local / RFC-1918 / ULA *IP literals* + the name `localhost`), and — the real defense — a send-time resolve-and-pin in `HttpWebhookTransport` to close **DNS-rebinding** (a public host that resolves to a private IP at delivery). Staged as its own change to avoid touching the S10 webhook path mid-S12; predicate specified in the audit + NOTES. Owner: integrations. |
| **I** | **Leaked signing secret → forged deliveries** | **Recorded debt (ADR-0011):** secret stored readable because we must sign with it. A leaked table lets an attacker forge. Production envelope-encrypts (KMS); the course keeps plaintext and names it. Tracked here. |
| **D** | One broken partner endpoint stalls the fan-out | Fan-out **does not throw** on partner failure; failures are logged rows, redelivered on demand (ADR-0011). |

## B6 — The org-less dispatcher (the classic blind spot)

The outbox dispatcher (`OutboxDrainService`) runs on a background thread with **no request, no
`ICurrentContext`**. This is exactly the code path `docs/concepts/multi-tenancy.md` told the reader
to remember. It is *why* ADR-0016 chose typed org-scoped ids over a bare global EF query filter: a
query filter reads the ambient context and **silently no-ops when there isn't one**, so a filter
that protects request handlers would quietly leave the dispatcher unfiltered. The dispatcher is the
documented, deliberate exception; every other by-id read carries its org explicitly.

---

## Findings roll-up (details in the audit report)

| ID | Boundary | Finding | Severity | Disposition |
|---|---|---|---|---|
| AF-3 | B2 | Cross-org worker read (IDOR) — `GetWorker` by bare id | **High** | **Fixed** (ledger #5 harvest) + regression tests |
| AF-7 | — | `double` FX fast path drifts on payroll-scale sums | **Medium** | **Fixed** (ledger #1 harvest) — path deleted |
| AF-6 | B1 | No login rate limiting / lockout | Medium | **Fixed** — throttle + backoff |
| AF-4 | B5 | Webhook SSRF egress (partner URL) | Medium | **Recorded (recommended)** — HTTPS belt today; host-block + send-time pin staged |
| AF-1 | B5 | Plaintext webhook signing secret | Low (accepted) | **Recorded debt** (KMS in prod) — ADR-0011 |
| AF-2 | B1 | API key has no last-used tracking | Low | **Recorded** — needs a column/migration; staged to avoid S12 conflict |
| AF-5 | B1 | Session fixation review | Info | **No action** — tokens are minted server-side per login, not accepted from the client |

## Exercises

1. Walk B5 with an expired-timestamp webhook signature replay (S10): which defense catches it, and
   in what order — the timestamp-in-signature, the HMAC, or the constant-time compare?
2. Delete the `CrossTenantReadAuditTests` sweep and add a new by-id use case with no context port.
   Which gate goes red — and which one *should* have but doesn't? (That's the B2 lesson.)
3. For AF-1, write the one-paragraph incident report from eighteen months after a DB backup leak.
