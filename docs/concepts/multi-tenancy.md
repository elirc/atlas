# Multi-Tenancy: One Database, Many Walls

**One-pager · Sprint 02 · pairs with ADR-0003**

## The problem

Atlas hosts many client orgs in one database. Every query that touches org-owned data must be
filtered by the caller's org — every single one, forever, including the ones written at 6pm on a
Friday in sprint 14. "Remember to filter" is not a strategy; it is a countdown.

## The design stance

Make tenancy an **invariant** with a small number of owners, instead of a habit with hundreds:

1. **Identity is resolved once** — the bearer-token middleware builds `ICurrentContext`
   (user, org, role) per request. Endpoints and use cases never parse headers.
2. **Use cases gate access** — `RequireUser/RequireOrg/RequireRole` sit at the top of each use
   case, next to the business logic they protect. Authorization you can see in the same file as
   the code it guards is authorization a reviewer can actually review.
3. **Repositories carry the filter** — org-owned queries take the org id from context, so the
   filter is in one method per aggregate, not sprinkled through call sites.
4. **The matrix is executable** — `AuthzMatrixTests` pins persona × endpoint → status. New
   endpoint, new rows. A tenancy regression is a red build, not a disclosure letter.

## 404 vs 403 — the leak most APIs ship

`GET /client-orgs/{someone-else's-id}` returns **404**, not 403. A 403 says "it exists, but not
for you" — and *existence* is data. With incrementing ids it enumerates your customer list; even
with GUIDs it confirms a guess. Rule of thumb: **outside your tenant, nothing exists.** Inside
your tenant, 403 is fine and useful ("you're an Employee; this needs an admin").

## Where this goes next

- Sprint 03 adds the first org-owned aggregate (workers) — watch the repository filter pattern.
- Sprint 10's partner API keys get org-scoped, too — same invariant, second credential type.
- Sprint 13 audits the whole surface and asks the scary question: *which query could run without
  a context?* (Background jobs are the classic answer. Remember that.)

## Try it

1. In `AuthzMatrixTests`, find the cross-org 404 case. Change the use case to throw
   `ForbiddenException` instead and read exactly which tests object, and why.
2. Write down every code path that could construct a repository without a request (hint: seeders,
   future jobs). Who provides *their* org context? Keep your answer for Sprint 13.
