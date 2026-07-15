# Sprint 13 — Hardening: Security Audit & Threat Model

**Goal:** read Atlas like an attacker, write it down like an engineer. **Harvests: ledger #1
(double FX) and ledger #5 (tenant IDOR)** plus a systematic sweep.

## Scope

**In:** **threat model** (`docs/security/threat-model.md`: assets, trust boundaries, STRIDE pass
over the API surface, webhook egress, partner keys); **the audit** (`docs/security/sprint-13-audit.md`)
with findings table — including the two harvests below, plus honest smaller findings (rate
limiting absent on login, session fixation review, webhook SSRF egress rules, API key last-used
tracking); fixes for all findings; **CI additions**: gitleaks secret scan + `dotnet list package
--vulnerable` audit step (fails on known-vuln); login rate limiting + lockout backoff;
authorization matrix test expanded to every endpoint (the fix that keeps #5 fixed).

**Out:** penetration-test theater, dependency pinning beyond lockfiles, WAF cosplay.

## The harvest arcs — in-PR commits

**Ledger #5 (tenant IDOR, planted S2/S3):**
1. "Audit finding AF-3: cross-org worker read" — RED: authz matrix test, org B token + org A
   worker id → expects 404, gets 200.
2. Fix: repository by-id methods require org context at the type level (`OrgScopedId<T>` or
   equivalent) — the compiler now refuses the unscoped query; sweep of all by-id paths.

**Ledger #1 (double FX, planted S4):**
3. "Audit finding AF-7: payroll-scale FX drift" — RED: CsCheck property, item-wise vs summed
   conversion diverges on the fast path (adversarial decimals found by the shrinker).
4. Fix: fast path deleted; benchmark in commit body showing the "optimization" bought nothing.

## Commit narrative (sketch)

1–4. Harvest arcs above · 5. `fix: rate limiting + lockout on login` · 6. `feat(ci): gitleaks +
vulnerable-package audit` · 7. `docs: threat model + audit report + ADR-0016 security posture`

## Teaching placements

- 📘 on the threat model: trust boundaries are where reviews concentrate; STRIDE as a checklist,
  not a religion.
- 📘 on the OrgScopedId fix: make the wrong thing unrepresentable > remember to filter.
- 🔍 on the audit doc: "findings tables need reproduction, impact, fix, AND regression test —
  a finding without a pinned test will regress."
- ⚠️ on the deleted fast path: performance claims without benchmarks are how correctness dies.
- 🔗 both harvests link their planting shas AND the blind-spot review comments that praised the
  neighborhood — "this is what LGTM misses; that was the lesson."

## Debate

`[Reviewer]`: "OrgScopedId infects every repository signature — heavy. A global EF query filter
on OrgId does the same invisibly." `[Author]`: invisible is the problem; query filters silently
no-op when the context is missing (background jobs, the outbox dispatcher!) and the S10
dispatcher runs org-less. The type makes the decision visible at every call site. Conclusion:
both — query filter as belt, typed ids as suspenders, dispatcher documented as the deliberate
exception. ADR-0016 records it.

## Exercises

- Run the authz matrix and count the endpoints. Which two were hardest to categorize? Why?
- Forge an expired-timestamp webhook signature replay (S10) — which defense catches it, and in
  what order?
- Pick any finding, delete its regression test, and write the one-paragraph incident report from
  eighteen months later.
