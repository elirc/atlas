# Sprint 02 — AuthN/AuthZ, Users, Roles, Multi-Tenancy

**Goal:** every request runs as someone, in exactly one org, with a role — and tenant scoping is a
cross-cutting policy, not per-handler boilerplate.

## Scope

**In:** User/Membership/Session; register/login/logout (hand-rolled session tokens, PBKDF2
hashing); roles OrgAdmin/HrManager/Employee/Contractor/AtlasOps; `ICurrentContext` (who + which
org + role) flowing into use cases; tenant-scoping repository pattern (org filter applied at the
repository boundary, not remembered per query); authorization policies per endpoint group;
`docs/http/auth.http`.

**Out:** partner API keys (S10), password reset, MFA.

## Teaching focus

AuthN vs authZ. Multi-tenancy as an *invariant* ("no query leaves without an org filter") and
where to enforce it so forgetting is hard. Why hand-rolled-then-hardened beats
library-first-understanding-never.

## Commit narrative (sketch)

1. `feat(domain): User, Membership, Role — invariants in constructors`
2. `feat(application): register/login use cases, ICurrentContext, IPasswordHasher port`
3. `feat(infrastructure): session store, PBKDF2 hasher, org-scoped repository base`
4. `feat(api): auth endpoints + endpoint filters resolving CurrentContext`
5. `test(api): authorization matrix — role × endpoint expectations`
6. `docs: ADR-0003 session auth + tenancy enforcement, concepts/multi-tenancy.md`

## Teaching placements

- 📘 tenancy one-pager link on the scoped-repository base class.
- 🔍 on the authz matrix test: "reviewers read the deny cases first."
- ⚠️ on the session token generation: timing-safe comparison, entropy source.
- 🔗 back to ADR-0001: why CurrentContext is a port, not a static.
- **Blind-spot comment (ledger #5 setup):** `[Reviewer]` praises the org filter on the LIST
  endpoints explicitly. Silence elsewhere.

## Debate

`[Reviewer]`: "Just use ASP.NET Identity." `[Author]`: Identity solves enterprise auth we don't
have (external logins, 2FA, stamps) and hides the two ideas this sprint teaches (credential
verification, session lifetime). We accept the cost of hand-rolling and schedule hardening (S13)
to audit it. Conclusion documented in ADR-0003 with an explicit "when we'd switch" trigger.

## Exercises

- Add a `ReadOnlyAuditor` role that can GET everything and mutate nothing, touching only the
  policy layer.
- Write the failing test you'd expect for cross-org access on any endpoint. Keep it.
- Explain why `ICurrentContext` must not be readable inside Domain entities.
