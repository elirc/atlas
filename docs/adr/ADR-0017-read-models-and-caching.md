# ADR-0017: Read Models, the N+1 Hunt, and the Cache Ledger

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 14)
- **Deciders:** [Author], [Reviewer]

> Numbering note: 0014–0016 are reserved for the S12 (payroll vendor + v2) and S13 (hardening)
> tracks integrating in parallel; reporting took the next free number by agreement. See meta/NOTES.md.

## Context

For thirteen sprints the object model was optimised for WRITES: aggregates with invariants, state
machines, one-thing-at-a-time. Executives ask different questions — headcount, cost, renewals,
compliance across the whole org — and answering them by loading aggregates is both the wrong shape
and the wrong cost. This ADR records three linked decisions: read models as projections, the N+1
those reports tempt you into (and how we caught it), and the two — exactly two — things we cache.

## Decision 1: read models are SQL projections behind `IReportQueries`, not aggregate traversal

Reports read through a dedicated port whose return types are flat records (`HeadcountRow`,
`EmploymentCostRow`, `UpcomingRenewalRow`, `WorkerComplianceInput`). The adapter projects the exact
columns a question needs — no `Include`, no change tracking, no entity graph. This is **CQRS-lite**:
the read path is split from the command path *without* a message bus, a second store, or an
eventual-consistency story. We take the half of CQRS that pays here (clarity, query-shaped reads) and
decline the half that doesn't (operational complexity we have no load to justify).

**The boundary the Reviewer extracted (and we accepted):** any figure used to make a DECISION INSIDE
the system — a gate, a guard, a calculation someone acts on — must live in the DOMAIN, not in a
report. These projections are read-only VIEWS for humans; the schema is their contract. Raw-ish SQL is
safe *here* precisely because nothing writes through it and nothing branches on it. The day a report's
number starts driving behaviour, it graduates to the domain. Recorded so it is not quietly crossed.

**Decimals still fold in memory.** SQLite cannot `SUM` decimal (the S7 wall); the cost projection
fetches amounts and folds in C#, in full precision, grouped by (country, currency) because Money
refuses to add across currencies. The SQL shapes the rows; the domain rule (Money) does the math.

## Decision 2: the N+1 hunt — measured, not asserted by faith

The shipped list endpoints were already N+1-free — S05–S11 built them as lightweight indexes on
purpose (the `// No Include` comments are the receipts). The N+1 that reporting *introduces* is the
per-row aggregate load: build the org compliance rollup the obvious way and you loop the workers,
asking the per-worker document repository once per worker — `1 + N` round-trips that grow with every
hire. We caught it with a **query-counting interceptor** (EF logging turned into a hard number) and a
test that asserts the COUNT, not a duration:

| Read path | Before (naive) | After (projection) |
|-----------|----------------|--------------------|
| Compliance roster (N workers) | `N + 1` queries | **2** queries |

The fix is not cleverer per-row code — it is a different query SHAPE: load the org's documents once,
group in memory, fold each worker through the *unchanged* `ComplianceEvaluator`. The test is
parameterised by worker count; the naive count scales and the fixed count does not, which is N+1
encoded as a regression guard. A perf commit without a before/after measurement is a vibe; this is
the meter.

## Decision 3: the cache ledger — exactly two entries, each with an invalidation trigger

A cache is a liability you take on deliberately. Every entry is documented here BEFORE it exists, with
two mandatory fields — a cache without both is a bug on a timer.

| # | What | Staleness budget | Invalidation trigger | Why it is safe |
|---|------|------------------|----------------------|----------------|
| 1 | Supported-country reference list | ≤ 10 min (TTL) | A new country ships (a deploy today; `Invalidate()` the day it becomes runtime) | Reference data — identical for every org; changes only on deploy (S11: a country is a code file) |
| 2 | FX conversions | ≤ 30 min (TTL) | A rate refresh from the source calls `Invalidate()` | A pure function of (amount, source, target); memoizing is trivially correct |

**What we NEVER cache: per-org mutable data** — workers, invoices, payroll, compliance status.
It changes on every request; its correct cache is no cache. A stale worker count is a wrong answer
wearing a fast one's clothes, and unlike a stale rate it fails *silently* until someone trusts it.

Entry #2 (FX) ships its decorator and invalidation contract in this sprint but is **wired with the
S12 FX-vendor swap**, not here — the cache belongs with the live vendor whose latency finally makes it
load-bearing, and wiring it now would fight S12 over the `IFxRates` registration. The contract exists
before the cache matters; that is the whole point of writing the ledger first.

## The debate: raw SQL vs the type safety we've preached for 13 sprints

- **[Reviewer]:** SQL projections abandon LINQ's compile-time safety — the thing this course sells.
- **[Author]:** the safety we preach is *domain* safety. Reports are read-only views where the schema
  IS the contract, and EF's LINQ-to-SQL translation is its own bug class at reporting complexity
  (silent client-eval, un-translatable expressions). Compromise: SQL-ish projections live behind
  `IReportQueries` with result records, integration-tested against SQLite AND audited for Postgres
  dialect. **Resolution:** accepted, with the Decision-1 boundary as the price — decision-making
  figures move to the domain.

## Consequences

- Adding a report is a projection method + a use case + an endpoint. No migration: read models are
  keyless projections over existing tables, so ModelMappingTests gains nothing to allow-list.
- The cost report is trustworthy because S7 froze payslips; the report cannot drift because the data
  under it cannot be edited (🔗 ADR-0008).
- Two caches, both invalidatable, both reference-data-only. The exercise deletes an invalidation line
  and traces the blast radius — do it once and the ledger stops feeling like bureaucracy.
