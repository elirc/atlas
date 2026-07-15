# Atlas — Product Spec & 15-Sprint Curriculum

**Version:** 1.0 · **Date:** 2026-07-14 · **Repo:** `elirc/atlas` · **Course:** 8 (first .NET course)

---

## 1. Purpose

Two goals; the second is the real one:

1. **Product:** a functional clone of a global-employment / EOR platform in the mold of Velocity
   Global's Pebl — hire, onboard, pay, and manage employees and contractors across countries
   without owning local legal entities. Functionality and domain only; never branding or assets.
2. **Learning:** every sprint is a GitHub PR engineered as a chapter of a C#/.NET curriculum
   focused on **business-logic design** and **API design**, organized around one thesis:

> **Software is optimized for change.** When rules change, vendors change, or integrators arrive,
> the diff should be small, local, and provably safe.

The product is the textbook. The PRs are the chapters. The architecture tests are the proctor.

## 2. Personas

| Persona | Role on the API |
|---|---|
| **Org Admin** | Owns a client org: billing, settings, user management |
| **HR Manager** | Hires, onboards, approves time off/expenses, views payroll |
| **Employee (EOR)** | Sees own profile, payslips, requests time off, submits expenses |
| **Contractor** | Submits invoices, sees payment status |
| **Atlas Ops** | Platform-side: compliance review, pay-cycle execution, country rules |

Personas are authorization scopes; there is no UI. Partner systems authenticate separately with
scoped API keys (Sprint 10).

## 3. Domain model (summary)

- **Country / CountryRulePack** — currency, employer-burden components, statutory leave minimums,
  notice-period rules, required document kinds. Rule packs are *pluggable*: adding a country adds
  files, never edits core (proved in S11).
- **ClientOrg / User / Membership** — multi-tenant; every row and every query is org-scoped.
- **Worker / EmploymentRecord** — a worker's terms (salary, title, hours) are **effective-dated**;
  changes append records, history is immutable.
- **Quote** — country + gross salary → total-cost-of-employment breakdown (gross, burden lines,
  atlas fee) in org currency and local currency via an FX-rates port.
- **Engagement (hiring lifecycle)** — `quote → offer → contract → onboarding → active` state
  machine, guarded transitions, append-only **ActivityLog**.
- **LeavePolicy / LeaveBalance / LeaveRequest** — accrual math (monthly proration, carryover
  caps), country minimum enforcement, approval workflow.
- **PayCycle / PayrollRun / Payslip** — per country+frequency; gross-to-net through the rule
  pack; run execution is idempotent; cycle state machine `draft → review → approved → executed`.
- **Expenseclaim** — through the generic **Approval** component (extracted S8 from S6).
- **ContractorInvoice** — submit → approve → pay via `IPaymentProvider` (mock vendor behind an
  anti-corruption layer).
- **ComplianceRequirement / WorkerDocument** — per-country required documents, expiry tracking,
  rollup status.
- **WebhookSubscription / OutboxMessage / ApiKey** — the integration surface.
- **Read models** — headcount, cost by country, upcoming renewals (S14).

## 4. API conventions (ADR-0002 expands)

- Versioned base path `/api/v1/...`; evolution by addition or new version, never breaking (S12
  demonstrates v2 of one resource).
- Errors: RFC 7807 `application/problem+json` with stable `type` URIs.
- Pagination: cursor-based (`?cursor=`, `?limit=`), `X-Next-Cursor` response header.
- Filtering/sorting: explicit whitelisted query params per resource.
- `Idempotency-Key` header honored on all money-moving POSTs (mandatory from S10 middleware).
- OpenAPI document committed at `docs/openapi/atlas-v1.json`; a contract test regenerates and
  diffs it — drift fails CI.
- Auth: session-token bearer auth for users (hand-rolled first, hardened later); scoped API keys
  for partners (S10).

## 5. Architecture

Hexagonal / ports-and-adapters, single solution, modular monolith:

```
src/Atlas.Domain           entities, value objects (Money, CountryCode, DateRange), state machines,
                           policies. ZERO framework dependencies.
src/Atlas.Application      use cases + ports (IClock, IFxRates, IPaymentProvider, IOutbox, repos).
src/Atlas.Contracts        versioned DTOs + webhook payloads. No logic.
src/Atlas.Infrastructure   EF Core (Postgres dev / SQLite tests), adapters, outbox dispatcher.
src/Atlas.Api              ASP.NET Core host: endpoints, auth, ProblemDetails, OpenAPI.
tests/                     Domain.Tests · Application.Tests · Api.Tests · ArchitectureTests
```

**The dependency rule is CI-enforced from Sprint 1** (`Atlas.ArchitectureTests`): Domain references
nothing; Application references only Domain; Api never touches Infrastructure types directly;
Contracts stay logic-free. Architecture tests are never weakened; if they fight, the design is
wrong.

Testing: xUnit; CsCheck property tests for Money arithmetic/allocation, accrual proration,
effective-date resolution. CI is DB-less, Docker-less, network-less.

## 6. The 15 sprints

**Phase 1 — MVP (S1–S5) → `v0.5.0`**

| # | Headline | Teaching focus |
|---|---|---|
| 01 | Walking skeleton: solution, countries+orgs vertical slice, arch tests, OpenAPI, CI | Hexagonal layout; fitness functions; API conventions |
| 02 | AuthN/authZ, users, roles, multi-tenancy | Tenant scoping as cross-cutting policy, not boilerplate |
| 03 | Workers + effective-dated employment records + audit log | "CRUD done excellently"; temporal data; append-only logs |
| 04 | Quotes & cost calculator: Money, FX port, rule packs v1 | Value objects; ports; money discipline (flagship #1) |
| 05 | Hiring lifecycle state machine → **v0.5.0** | Explicit state machines; guarded transitions |

**Phase 2 — Full product (S6–S15) → `v1.0.0`**

| # | Headline | Teaching focus |
|---|---|---|
| 06 | Time off: accruals, proration, country minimums | Date math; the clock as a port (harvest arc) |
| 07 | Payroll engine: gross-to-net, cycles, idempotent execution | Allocation without lost pennies (flagship #2) |
| 08 | Expenses via extracted generic Approval component | Reuse by extraction, not prediction |
| 09 | Contractor invoicing + mock payment provider | Anti-corruption layers; vendor faults |
| 10 | Integration surface: webhooks + outbox + API keys + idempotency middleware | At-least-once delivery done honestly (integration flagship) |
| 11 | Compliance + rule packs v2 as plugins; add 2 countries, zero core changes | The thesis, proved with a diff |
| 12 | **The pivot sprint**: second payroll vendor mid-quarter + API v2 of one resource | The thesis exam; contract evolution |
| 13 | Hardening: security audit, threat model, flaw harvests | Reading code like an attacker |
| 14 | Reporting & performance: read models, N+1 hunt, caching | Query shape vs object shape |
| 15 | Production readiness: health, logging, runbooks, postmortem, retrospective → **v1.0.0** | Operating what you built |

## 7. Teaching machinery

- **Per-sprint ritual:** milestone + story issue + 3–6 task issues → branch `sprint-NN-slug` →
  ordered commits (domain → application → adapters → endpoints → docs; RED→GREEN arcs as real
  history) → PR from body file → 8–11 tagged inline comments (📘/🔍/⚠️/🔗, personas `[Author]`
  `[Reviewer]` `[Stakeholder]`) → one design-debate thread → exercises → squash-merge → close
  milestone.
- **ADR per sprint** (`docs/adr/`), numbered by sprint from S1; plus one `docs/concepts/`
  one-pager per sprint.
- **Planted flaws:** exactly five, seeded silently, harvested with failing tests in later sprints
  (≥2 in S13). Ledger is author-private (`FLAW-LEDGER.local.md`, gitignored) until the S15 reveal
  commits it as `docs/sprints/flaw-ledger.md`.
- **Commit body format:** summary line; `Why this way:`; `Notice:`; `See also:`; `Refs #N`.
- **Code markers:** `// LEARN:` `// WHY:` `// GOTCHA:` `// TRY:`.

## 8. Out of scope

Real payments, real emails, real KYC, real tax rates (shapes are realistic; numbers are
illustrative), frontend UI, i18n, mobile. Vendor calls exist only as in-repo mocks behind ports.
