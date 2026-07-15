# ADR-0003: Hand-Rolled Session Auth + Tenancy as an Invariant

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 02)
- **Deciders:** [Author], [Reviewer]

## Context

Every org-owned row in Atlas (workers, quotes, payroll…) belongs to exactly one ClientOrg, and a
tenancy leak in an EOR product is a company-ending incident, not a bug. Separately: the course
must teach *what authentication is*, which libraries deliberately hide.

## Decision 1: hand-rolled sessions, hardened later

PBKDF2-SHA256 (600k iterations, per-hash salt, self-describing format) + opaque bearer tokens
(256-bit CSPRNG, `atls_` prefix, SHA-256 at rest, 12h lifetime, revocable). No ASP.NET Identity,
no JWTs.

- **Why not Identity:** it solves enterprise auth we don't have (external logins, 2FA, security
  stamps) and hides the two ideas this sprint teaches: credential verification and session
  lifetime. **Switch trigger** (recorded): the day we need SSO/OIDC, we adopt the platform's
  `AddAuthentication` pipeline rather than growing our own.
- **Why not JWTs:** we need server-side revocation anyway (logout, S13 hardening), so
  self-describing tokens buy nothing and add an expiry-vs-revocation split brain.
- Password rules: length only (min 10) per NIST 800-63B; composition rules push users to
  `Password1!`. Breach-list checking is S13 backlog.
- Login failures are uniform ("Invalid email or password") — distinct errors are a
  user-enumeration oracle. The tests assert byte-identical failure bodies.
- A presented-but-invalid token is 401, never silently anonymous — stale clients must learn to
  re-login from the status code.

## Decision 2: tenancy is an invariant with one owner per layer

- **Application:** `ICurrentContext` (who, which org, what role) is a port; use cases call
  `RequireUser/RequireOrg/RequireRole/RequireAtlasOps`. Authorization lives IN use cases, next to
  the logic it guards — not in endpoint attributes where it can drift from the code it protects.
- **Cross-org access is 404, not 403.** A 403 confirms existence; existence across tenants is
  need-to-know. In-org permission failures are 403.
- **Org roles ≠ platform roles.** `Role` (OrgAdmin/HrManager/Employee/Contractor) is per
  membership; `IsAtlasOps` is a user-level flag. Ops passes role checks (support workflows) and
  every ops action is attributable via the S3 audit log.
- **One membership per user** for now — a simplification that keeps `ICurrentContext.OrgId`
  scalar. Multi-org users (agencies) are a known future; the Membership table already supports
  it, only context resolution would change.
- **The authz matrix test** (`AuthzMatrixTests`) is the fitness function for this ADR: every new
  endpoint must add its persona × status rows (CONTRIBUTING rule; audited in S13).

## Consequences

- Auth code is ~300 lines we own and must audit (scheduled: S13). Accepted for the teaching value.
- The uniqueness race on email registration is closed by the DB unique index, not the
  application-level check — the check is UX, the index is the truth.
- `X-Next-Cursor`, problem types, and now `WWW-Authenticate: Bearer` are contract surface —
  the OpenAPI document and drift gate picked up all endpoint changes this sprint.

## Rejected alternatives

- **ASP.NET Identity / IdentityServer / Auth0:** see switch trigger above.
- **JWT access + refresh tokens:** two token lifetimes and a revocation list to teach *later*
  (S10 partner keys are the stateless-credential lesson, with scopes).
- **Global EF query filters for tenancy, now:** revisited in S13 — filters silently no-op when
  the context is absent (background jobs), which is exactly where leaks hide. The S13 audit
  weighs typed org-scoped ids + filters together.
