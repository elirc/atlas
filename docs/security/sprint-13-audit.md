# Sprint 13 Security Audit — Findings & Fixes

**Pairs with `threat-model.md` and ADR-0016.** Scope: the auth, tenancy, vendor, and webhook
surfaces described in the threat model. This is the *report* — what we looked for, what we found,
and for each finding: **reproduction · impact · fix · regression test.**

## 🔍 [Reviewer] Why every row carries a pinned test

A findings table without a regression test is a list of bugs you will re-introduce. "We fixed the
cross-org read" is a sentence; `WorkerTenancyTests.A_caller_in_another_org_cannot_read_a_workers_detail_by_id`
is a wall. The rule for this document: **a finding is not closed until a test would go red if it
came back.** Two findings this sprint are genuine harvests — a planted flaw with a real
RED→GREEN arc — and they anchor the method.

## Findings at a glance

| ID | Title | Severity | Status | Regression test |
|---|---|---|---|---|
| **AF-3** | Cross-org worker read (IDOR) | High | Fixed | `WorkerTenancyTests`, `CrossTenantReadAuditTests`, matrix row |
| **AF-7** | `double` FX fast path drifts on payroll sums | Medium | Fixed | `FxRateTablePropertyTests` |
| AF-6 | No login rate limiting / lockout | Medium | Fixed | `LoginThrottleTests`, `LoginRateLimitTests` |
| AF-4 | Webhook SSRF egress | Medium | Recorded (recommended) | — (predicate + test sketch below) |
| AF-1 | Plaintext webhook signing secret | Low | Accepted (recorded) | — (ADR-0011) |
| AF-2 | API key has no last-used tracking | Low | Recorded | — (needs migration) |
| AF-5 | Session fixation review | Info | No action | `AuthFlowTests` (tokens minted server-side) |

---

## AF-3 — Cross-org worker read (IDOR) · **HARVEST (ledger #5)**

**Where:** `src/Atlas.Application/Workforce/GetWorker.cs`. Planted Sprint 03 (commit `d5e4db3`).

**Reproduction.** As an admin of Org B, call the use case with a worker id belonging to Org A:

```csharp
var getWorker = new GetWorker(workers, StubContext.OrgUser(OrgB, Role.OrgAdmin));
Worker leaked = await getWorker.ExecuteAsync(orgAWorkerId, ct); // returns Org A's worker
```

The original code was:

```csharp
context.RequireUser();                                   // proves you are SOMEBODY
Worker? worker = await workers.FindAsync(id, ct);         // by primary key — NO org filter
```

`RequireUser()` authenticates; `FindAsync(id)` reads by primary key across all tenants. Together:
any authenticated caller reads any worker by id.

**Why it hid.** The HTTP endpoint `GET /workers/{id}` pairs `GetWorker` with `GetWorkerTerms`,
which **is** org-scoped and 404s first — so at the API boundary a cross-org read already returned
404. The leak was only observable by driving the use case directly. This is the lesson: the authz
**matrix is necessary but not sufficient**; it passed on this endpoint before AND after the fix.
The blind-spot was foreshadowed in the S02/S03 reviews, which praised the *list* endpoint's scoping
and said nothing about the by-id path (`docs/concepts/multi-tenancy.md`: "which query could run
without a context?").

**Impact.** Full cross-tenant disclosure of worker PII and, via the detail view, pay history. In an
EOR product this is company-ending. **High.**

**Fix.**

```csharp
context.RequireRole(Role.OrgAdmin, Role.HrManager);                 // detail = HR-and-up (like GetEngagement)
Worker? worker = await workers.FindInOrgAsync(id, context.RequireOrg(), ct); // (id AND orgId)
return worker ?? throw new NotFoundException("worker", id);         // cross-tenant → 404, need-to-know
```

`FindInOrgAsync` already existed and is used by every sibling read (`GetQuote`, `GetEngagement`);
the fix is to use the tenant-safe method that was there all along, and to gate the pay-bearing
detail view to HR-manager-and-up.

**Regression tests.**
- `WorkerTenancyTests` (Application) — the exposing test: Org B caller + Org A id → `NotFoundException`;
  Employee → `ForbiddenException`; admin/HR read their own.
- `CrossTenantReadAuditTests` (Application) — a fitness sweep: **every** by-id use case injects
  `ICurrentContext`. Necessary-but-not-sufficient, and it says so.
- `AuthzMatrixTests.Worker_detail_by_id_is_org_scoped` (Api) — the standing HTTP guard.

**Sweep (other by-id reads).** We walked every use case whose `ExecuteAsync` takes a `Guid` first
(the `CrossTenantReadAuditTests` set). Results:

- **Org-scoped via `FindInOrgAsync`:** `GetQuote`, `GetEngagement`, `GetInvoice`, `GetExpense`,
  `GetPayrollRun`, `GetPayslip`, `GetWorkerTerms`, and every by-id *command* (approvals,
  transitions, revoke/pause/redeliver). ✅
- **Org-scoped via an explicit context guard (not the repo method):** `GetClientOrg` compares
  `context.OrgId != id` before a by-pk `FindAsync` — correct because a `ClientOrg` **is** the
  tenant, but the double pattern is worth noting; it is the one place a by-pk read is safe.
- **Self-scoped (id comes from the authenticated principal, not the request):** `GetMe`,
  the bearer middleware's `users.FindAsync(session.UserId)`. Not attacker-controlled. ✅
- **Only offender:** `GetWorker`. Fixed above.

---

## AF-7 — `double` FX fast path drifts on payroll-scale sums · **HARVEST (ledger #1)**

**Where:** `src/Atlas.Infrastructure/Fx/FxRateTable.cs`. Planted Sprint 04 (commit `d232b1c`).

**Reproduction.** The adapter carried a "fast path" for amounts under 1,000:

```csharp
if (Math.Abs(amount.Amount) < FastPathThreshold)
{
    double converted = (double)amount.Amount * (double)usdFrom / (double)usdTarget; // base-2 cross
    return Money.Of((decimal)converted, target).Rounded();
}
```

The exposing property (`FxRateTablePropertyTests`) states the law **"a conversion equals the exact
decimal cross,"** and a shrinker-style search over half-cent boundaries finds adversarial decimals
where the `double` cross rounds to the wrong cent:

| amount | pair | double path | exact decimal |
|---|---|---|---|
| `10.80540000000001` | USD→EUR | `10.00` | `10.01` |
| `1.004629629629629629629630` | EUR→USD | `1.08` | `1.09` |
| `1.396648044692737430167598` | PHP→USD | `0.02` | `0.03` |

And the accumulation that gives the finding its name: **200 line items** of one adversarial amount
sum to **4.00** on the fast path vs **6.00** exact — a **two-euro drift on one small batch**, and it
grows with the batch.

**The honest twist (⚠️, and the real lesson).** For ordinary cent-aligned inputs the `double` path
happened to agree with decimal, because `(decimal)converted` re-rounds to 15 significant digits and
absorbs the error. So the "optimization" was invisible in day-to-day numbers *and* it had **no
measured benefit** — decimal division was never a bottleneck. It was a performance claim with no
benchmark, sitting on top of a correctness landmine that only adversarial inputs (and a live FX
vendor's higher-precision rates, coming in S12) detonate. **A performance claim without a benchmark
is a correctness risk in a speed costume.**

**Impact.** Sub-cent-per-item drift that accumulates across payroll-scale conversions; wrong money,
silently. **Medium** (bounded per item, unbounded in aggregate).

**Fix.** Delete the fast path. One exact decimal path remains; the same-currency identity still
returns a **fresh, rounded** `Money` (the S04 EF-tracking constraint — two owned values on one
aggregate must be distinct CLR instances).

**Regression test.** `FxRateTablePropertyTests` — 13 adversarial `[Theory]` cases across every pair,
a batch-drift `[Fact]`, a deterministic boundary-cloud scan, the same-currency-fresh-instance
`[Fact]`, and a large-amount exact-path pin. RED on the fast path, GREEN once deleted.

**Blind spot (🔗).** The S04 review praised the **port design** on this very file (`IFxRates` —
"ports earn rent later") and said nothing about the `double` inside the adapter. LGTM on the
architecture; the arithmetic walked through.

---

## AF-6 — No login rate limiting / lockout · Fixed

**Reproduction.** Before this sprint, `POST /auth/login` answered unlimited wrong-password attempts
with a 401 each — an online brute-force / password-spray surface (ADR-0003 recorded it as S13 work).

**Fix.** `ILoginThrottle` port + pure `LoginThrottlePolicy` (5 failures per 15-min window → lockout
with exponential backoff 30s→…→15m cap) + in-memory adapter. `Login` gates **before** verifying, so
a locked identifier gets a **429 with Retry-After** and its password is never checked. Keyed on the
*submitted, normalized* email so lockout is not an enumeration oracle and a password-spray across
guessed addresses trips the same backoff.

**Regression tests.** `LoginThrottleTests` (pure policy + use-case ordering/normalization),
`LoginRateLimitTests` (HTTP 429 + Retry-After; correct password still locked; other accounts
unaffected). **Curtain:** account-lockout-as-DoS and multi-node counter sharing are recorded in
ADR-0016.

## AF-4 — Webhook SSRF egress · Recorded (recommended)

**Reproduction.** `RegisterWebhook` accepts any absolute **https** URL (HTTPS is already enforced in
`WebhookSubscription.Create`). Nothing blocks `https://169.254.169.254/…` (cloud metadata),
`https://127.0.0.1/…`, or an RFC-1918 host — Atlas would sign and POST to them.

**Recommended fix (staged — do NOT land mid-S12 on the webhook path).**
1. Registration-time host block in `WebhookSubscription.Create`: reject when the URI host is an IP
   *literal* in loopback / link-local / RFC-1918 / unique-local ranges, or the literal name
   `localhost`. Pure-domain, one predicate, testable in `WebhookSubscriptionTests`.
2. The **real** defense (closes DNS-rebinding, which #1 cannot): in `HttpWebhookTransport`, resolve
   the host, reject if any resolved address is private, and **pin** the connection to the vetted IP
   at send time. This is where SSRF is actually stopped.

Deferred to its own change because it touches the S10 webhook path S12 is adjacent to; specified
here and in NOTES so it lands with a test, not as a scramble.

## AF-1 — Plaintext webhook signing secret · Accepted (recorded)

Named already in ADR-0011 and on `WebhookSubscription` itself: the secret is stored readable because
Atlas must sign with it. A leaked table → forged deliveries. Production envelope-encrypts (KMS); the
course keeps plaintext and names it. gitleaks (new this sprint) scans the **repo**, not the DB — it
cannot cover this, and `.gitleaks.toml` says so.

## AF-2 — API key has no last-used tracking · Recorded

`ApiKey` records created/revoked but never "last used." Without it, a leaked-but-idle key can't be
spotted and pruned. Fix is a `LastUsedAtUtc` column stamped in the bearer middleware — **a
migration**, deliberately staged out of S13 to avoid colliding with S12's schema work. Recorded, not
done.

## AF-5 — Session fixation · No action

Reviewed and closed with no change: Atlas never accepts a client-supplied session id. Tokens are
minted server-side (`ISessionTokens.Mint`) on each successful login and hashed at rest; there is no
pre-auth token to fixate. `AuthFlowTests` pins the mint-on-login behavior.

## Exercises

1. Run `CrossTenantReadAuditTests`, count the by-id use cases, and name the two that were hardest to
   categorize (hint: `GetClientOrg` and the org-less dispatcher). Why is each an exception?
2. Delete `FxRateTablePropertyTests` and reinstate the `double` fast path. Write the one-paragraph
   incident report from eighteen months later, after a payroll reconciliation is off by €40.
3. Implement AF-4 step 1 and prove with a test that `https://127.0.0.1/hook` is rejected — then
   explain in two sentences why your test does **not** prove Atlas is safe from SSRF.
