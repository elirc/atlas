# Sprint 01 — Walking Skeleton

**Goal:** the thinnest possible end-to-end slice — countries and client orgs over real
architecture — with every quality gate already armed.

## Scope

**In:** `Atlas.sln` (5 src + 4 test projects); Countries (seed-only reference data: list/get) and
ClientOrgs (create/list/get) vertical slice; EF Core with SQLite-for-tests / Postgres-for-dev;
NetArchTest dependency-rule suite as a CI gate; OpenAPI generated + committed + drift contract
test; ProblemDetails error mapping; cursor pagination on list endpoints; docker-compose for dev
Postgres; seed command; `docs/http/orgs.http`.

**Out:** auth (S2 — endpoints are anonymous for one sprint and say so loudly), workers, quotes.

## Teaching focus

The hexagon as *executable* rules, not a diagram. Fitness functions. Why the walking skeleton
carries conventions (pagination, ProblemDetails, OpenAPI drift) before it carries features.

## Commit narrative (sketch)

1. `chore: solution + project skeletons with dependency rule wired` — empty projects, references,
   ArchitectureTests RED-capable (one deliberately failing assertion demoed in body, then fixed).
2. `feat(domain): Country + ClientOrg with CountryCode value object`
3. `feat(application): use cases + repository ports + pagination primitives`
4. `feat(infrastructure): EF Core model, SQLite/Postgres providers, seed`
5. `feat(api): endpoints, ProblemDetails, OpenAPI + drift contract test`
6. `docs: ADR-0001 hexagonal architecture, ADR-0002 API conventions, concepts/fitness-functions.md`

## Teaching placements

- 📘 on ArchitectureTests: fitness functions — architecture you can fail.
- 📘 on CountryCode: value objects vs primitive obsession, first contact.
- 🔍 on the endpoint file: "an endpoint should read like a table of contents."
- ⚠️ on EF configuration: why Domain doesn't reference EF (and what it costs).
- 🔗 OpenAPI drift test ← ADR-0002.

## Debate

`[Reviewer]`: "Nine projects for two tables is ceremony. Fold Contracts into Api and
Application into Domain until it hurts." `[Author]` defends: boundaries are cheapest on day one;
the pivot sprint (S12) will invoice the difference. Reviewer concedes conditionally — "if S12's
diff isn't local, I was right."

## Exercises

- Add a `regions` list endpoint without touching Infrastructure. What blocked you? (Nothing —
  explain why.)
- Make Application reference Infrastructure and read the architecture-test failure.
- Break the committed OpenAPI file on purpose; watch the drift test name the exact path.
