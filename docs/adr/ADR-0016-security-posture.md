# ADR-0016: Security Posture — Hardening, Tenancy Enforcement, and CI Gates

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 13)
- **Deciders:** [Author], [Reviewer]
- **Supersedes nothing; extends** ADR-0003 (auth + tenancy) and ADR-0011 (webhooks).

## Context

S13 is the hardening sprint ADR-0003 promised ("~300 lines of auth we own and must audit"). A
structured pass over the auth/tenancy/vendor/webhook surfaces (`docs/security/threat-model.md`)
turned up two planted-flaw harvests — **AF-3** (cross-org worker read, ledger #5) and **AF-7**
(`double` FX drift, ledger #1) — plus honest smaller findings. This ADR records the decisions the
audit forced, especially the one with the longest tail: **how tenancy is enforced so AF-3 cannot
recur.**

## Decision 1: fix AF-3 with the method that already existed, then make the gap un-hideable

`GetWorker` read by bare primary key. The immediate fix is not clever: use `FindInOrgAsync` (already
present, already used by every sibling read) and gate the pay-bearing detail to HR-manager-and-up.
The *durable* fix is three layers of guard, because AF-3 slipped past a one-layer defense:

1. **Per-use-case cross-org tests** (`WorkerTenancyTests`) — the sufficient guard. Drives an org-B
   caller at an org-A id and demands 404.
2. **A fitness sweep** (`CrossTenantReadAuditTests`) — every by-id use case injects `ICurrentContext`.
   Necessary, **not** sufficient (GetWorker *had* the context and still leaked) — and it says so.
3. **The authz matrix** (`AuthzMatrixTests`) — the standing HTTP guard. It missed AF-3 (the endpoint
   404'd via a paired scoped lookup), which is exactly why layers 1–2 exist.

## Decision 2: typed org-scoped ids AND a query filter — belt and suspenders — staged

The Reviewer's live objection is the crux of the sprint:

- **[Reviewer]:** "`OrgScopedId<T>` on every repository signature is heavy — it infects the whole
  data layer. A single global EF query filter on `OrgId` does the same thing invisibly, in one place."
- **[Author]:** invisible is the problem. A global query filter reads the ambient context and
  **silently no-ops when there is no context** — which is precisely the situation in background work:
  the S10 outbox dispatcher (`OutboxDrainService`) runs **org-less**, by design. A filter that
  protects request handlers would quietly leave the dispatcher unfiltered, and the leak would move
  from a code path we test to one we don't. A typed, org-scoped id makes the decision **visible at
  every call site**: you cannot construct the query without stating whose org it is.
- **Resolution — both, and name the exception.** Query filter as the *belt* (defense in depth for
  request-scoped reads); typed org-scoped ids as the *suspenders* (the wrong thing becomes
  unrepresentable at the call site); the outbox dispatcher documented as the deliberate org-less
  exception. **Make the wrong thing unrepresentable beats "remember to filter."**

**What S13 actually lands vs stages.** S13 lands the *behavioral* fix (AF-3 closed, three guard
layers) — a small, reviewable diff. The mechanical rollout of `OrgScopedId<T>` across every
repository signature, and the global query filter, are **staged to a dedicated follow-up**: they
touch every data-access file, and S12 (payroll vendor port + API v2) is integrating in parallel —
a repository-wide signature change now is a merge-conflict machine for no behavioral gain this
sprint. Recorded, with the design fixed here so the follow-up is mechanical, not exploratory.

## Decision 3: login hardening as a port, in-memory today

Rate limiting + lockout (AF-6) is an `ILoginThrottle` **port** with a pure `LoginThrottlePolicy`
(5 failures / 15-min window → exponential backoff 30s → 15m cap) and an in-memory adapter. The use
case gates **before** verifying credentials → **429 + Retry-After**, never checking a locked
account's password. Keyed on the *submitted, normalized* identifier so the lockout is not a
user-enumeration oracle (the same principle ADR-0003 defends with uniform 401s).

- **Curtain (recorded):** the in-memory adapter is per-process; multi-node deployment needs a shared
  store (Redis `INCR`+TTL, or the DB) — the port makes that a one-adapter swap, no use-case change.
- **Curtain (recorded):** account-lockout-as-DoS (an attacker locks a victim by spraying bad
  passwords). Backoff is capped, not permanent; production adds IP-scoped limits + CAPTCHA.

## Decision 4: two CI security gates, because a finding without a gate regresses

- **gitleaks** secret scan (history, not just tip), with an in-repo `.gitleaks.toml` allowlist that
  names the course's *fixture* secrets narrowly so a real leak still fires.
- **`dotnet list package --vulnerable --include-transitive`**, which we grep and fail on — the
  command exits 0 even when it finds problems. Transitive included: a vuln three levels down still
  ships in our bytes.

Each new dependency still needs a one-line justification in its ADR (CONTRIBUTING). This sprint adds
no runtime NuGet dependency; the FX property test uses xUnit theories with adversarial decimals
rather than pulling CsCheck into `Atlas.Api.Tests` (CsCheck stays scoped to `Atlas.Domain.Tests`,
per ADR-0005).

## Recorded debts / curtains (owner + trigger)

| Debt | Owner | Trigger to pay it |
|---|---|---|
| `OrgScopedId<T>` + global query filter rollout | platform | after S12 merges; own PR |
| Plaintext webhook signing secret (AF-1, ADR-0011) | integrations | KMS/envelope-encryption before real partners |
| Webhook SSRF host-block + send-time pin (AF-4) | integrations | before external webhook GA |
| API key `LastUsedAtUtc` (AF-2) | platform | next schema migration window (not during S12) |
| Login throttle shared store (AF-6 curtain) | platform | second API node / horizontal scale |
| Breach-list password check (ADR-0003 backlog) | platform | before self-serve signup GA |

## Consequences

- AF-3 and AF-7 are closed with genuine RED→GREEN arcs and pinned regression tests.
- Tenancy now has three guard layers, and the matrix's blind spot is documented rather than trusted.
- CI is red on a leaked secret or a known-vulnerable package.
- The typed-id/query-filter decision is *made* and *recorded*; the rollout is a scheduled follow-up,
  not an open question.
