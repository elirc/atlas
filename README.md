# Atlas — a Global Employment (EOR) Platform, Built as a Curriculum

Atlas is an **Employer of Record (EOR) platform** in the mold of Velocity Global's Pebl: it lets a
company (a "Client Org") hire, onboard, pay, and manage employees and contractors across countries
without owning a legal entity in each one.

It is also — and primarily — a **teaching repository**. The product is real, but the way it was
built is the point: **15 sprints, each shipped as one GitHub Pull Request** engineered as a chapter
of a course that takes a junior engineer toward mid/senior level in **C#/.NET, business-logic
design, and API design**.

> **Course 8** in the git-connected upskilling series. Predecessors (JS/TS): GitJira, Tracer,
> Relay, Folio, Harbor, Pulse. Atlas is the first .NET course.

## The thesis

> **Software is optimized for change.**

Every architectural decision in Atlas serves one question: *when the business rules change — and in
global employment they change constantly — how small is the diff?* The course proves the thesis
mechanically, not rhetorically:

- **Architecture-fitness tests** (`tests/Atlas.ArchitectureTests`) enforce the dependency rule in
  CI from Sprint 1. Violate the hexagon and the build fails.
- **Country rule packs** are plugins: Sprint 11 adds two countries with **zero core-code changes**
  and shows the diff to prove it.
- **Sprint 12 is a pivot exam**: a real requirement change (a second payroll vendor, clients
  switching mid-quarter) lands, and the PR *measures* diff locality per layer.
- The **integration surface** (versioned REST + OpenAPI contract, HMAC-signed webhooks over a
  transactional outbox, Idempotency-Key middleware, scoped partner API keys) treats "other
  software will integrate with this" as a first-class requirement, not an afterthought.

There is **no frontend**. The API, its contract, and its integration surface *are* the product.

## What Atlas does

| Domain | Highlights |
|---|---|
| Countries & rule packs | Employer-burden %, notice periods, leave minimums, currency — pluggable per country |
| Client orgs & users | Multi-tenant from day one; every query tenant-scoped |
| Workers & employment | Effective-dated employment terms; salary history is never overwritten |
| Quotes | Total-cost-of-employment calculator: gross, employer burden, fees, multi-currency |
| Hiring lifecycle | quote → offer → contract → onboarding → active, as an explicit state machine |
| Time off | Policies, accrual math (proration, carryover caps), country minimum enforcement |
| Payroll | Pay cycles, gross-to-net via rule packs, payslips, idempotent execution |
| Expenses | Reimbursements through a generic, reusable Approval component |
| Contractor invoicing | Submit → approve → pay via a mock payment provider behind an anti-corruption layer |
| Compliance | Required documents per country, expiry tracking, status rollups |
| Reporting | Headcount/cost/renewals as read models, not entity-graph traversal |

Personas: **Org Admin**, **HR Manager**, **Employee**, **Contractor**, **Atlas Ops** (platform
side). They exist as authorization scopes on the API.

We clone the *functionality and domain* of platforms like Pebl — never branding, copy, or assets.

## Stack

- **C# / .NET 10**, nullable reference types on, `TreatWarningsAsErrors` everywhere
- **ASP.NET Core** minimal-API host, RFC 7807 `application/problem+json` errors, OpenAPI committed
- **EF Core** — Postgres for dev (docker-compose), SQLite in-memory for tests; **CI needs no
  database, no Docker, no network**
- **xUnit** + property-based tests (CsCheck) for money math, accruals, effective dating
- **Hexagonal (ports & adapters)**: `Domain` ← `Application` ← `Infrastructure`/`Api`, with
  `Contracts` as the logic-free DTO edge

```
src/
  Atlas.Domain           pure business logic — zero framework dependencies
  Atlas.Application      use cases + ports (IClock, IFxRates, IPaymentProvider, repositories, ...)
  Atlas.Contracts        versioned request/response DTOs + webhook payloads
  Atlas.Infrastructure   EF Core, adapters, outbox dispatcher, migrations
  Atlas.Api              ASP.NET Core host: endpoints, auth, ProblemDetails, OpenAPI
tests/
  Atlas.Domain.Tests | Atlas.Application.Tests | Atlas.Api.Tests | Atlas.ArchitectureTests
```

## How to study this repo

Read **[LEARNER-GUIDE.md](LEARNER-GUIDE.md)** first. Short version: study each sprint's **merged
PR** in order — stakeholder issue → commits one by one → inline review comments → design-debate
thread → exercises. The playbooks in `docs/sprints/` say what each sprint teaches; the ADRs in
`docs/adr/` record why the code is shaped the way it is.

Milestones: `v0.5.0` (MVP, after Sprint 5) and `v1.0.0` (after Sprint 15).

## Running Atlas

```bash
docker compose up -d          # local Postgres
dotnet run --project src/Atlas.Api -- migrate seed   # schema + demo org
dotnet run --project src/Atlas.Api                   # Swagger UI at /swagger in Development
```

Every feature area has a committed `.http` file under `docs/http/` so you can replay each flow.

Verify the whole solution the way CI does:

```bash
dotnet build -warnaserror && dotnet test && dotnet format --verify-no-changes
```
