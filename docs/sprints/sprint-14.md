# Sprint 14 — Reporting & Performance: Read Models, N+1, Caching

**Goal:** answer the questions executives actually ask — headcount, cost, renewals — without
letting the object model dictate query shape. Then find and fix the queries that were lying about
their cost.

## Scope

**In:** three **read models** (SQL projections, not entity traversal): headcount by
country/status over time, employment cost by org/country/month (payroll-derived), upcoming
renewals/expiries (contracts + documents, next 90 days); reporting endpoints (`/api/v1/reports/...`)
with explicit query params, CSV export via streaming (`IAsyncEnumerable`); **the N+1 hunt**:
EF query logging turned into evidence — capture the worst three offenders from earlier sprints
(engagement timeline, compliance rollup, approval inbox), show query counts before/after
(`.Include`/projection rewrites) in commit bodies; **caching** for rule-pack registry + FX table
with explicit invalidation rules (and a one-pager on why we cache exactly two things);
`docs/http/reports.http`.

**Out:** BI tooling, materialized views, background report generation, dashboards.

## Teaching focus

Query shape ≠ object shape: the aggregate that's right for writes is wrong for reports. Measuring
before optimizing (query logs as commit-body evidence). Caching as a liability you take on
deliberately, with invalidation written down BEFORE the cache is added.

## Commit narrative (sketch)

1. `feat(application): report queries as SQL projections + IReportQueries port`
2. `feat(api): report endpoints + streaming CSV export`
3. `perf: engagement timeline N+1 — 41 queries → 2` (query log before/after in body)
4. `perf: compliance rollup + approval inbox — same treatment`
5. `feat(infrastructure): rule-pack + FX caching with invalidation on write`
6. `docs: ADR-0017 read models & the cache ledger, concepts/query-shape.md`

## Teaching placements

- 📘 on read models: CQRS-lite without the tax — separate the read path, skip the message bus.
- 📘 on the cache ledger: every cache entry documented with its invalidation trigger and its
  staleness budget; a cache without both is a bug on a timer.
- 🔍 on commit 3: "perf commits without measurements are vibes; before/after or it didn't happen."
- ⚠️ on streaming CSV: memory-bounded exports; what happens at row 10 when row 11 throws
  (headers already sent — the error contract of streams).
- 🔗 payroll cost model ← S7 payslips ("immutable executed runs are why this report can be trusted").

## Debate

`[Reviewer]`: "Raw SQL projections abandon the type safety we've been preaching for 13 sprints."
`[Author]`: the type safety we preach is *domain* safety; reports are read-only views where the
schema IS the contract, and EF's LINQ-to-SQL translation is its own bug class at this complexity.
Compromise: SQL lives behind `IReportQueries` with result records, integration-tested against
SQLite AND documented as Postgres-dialect-audited. Reviewer extracts a promise: any report used
for *decisions inside the system* must move to the domain. Recorded.

## Exercises

- Add "average time in onboarding by country" as a fourth report. SQL first or domain first? Why?
- Turn on EF query logging and find a fourth N+1 we didn't fix. (There is at least one.) Fix it.
- Delete the FX cache invalidation line and describe the bug's blast radius. How long until
  someone notices? What would have caught it?
