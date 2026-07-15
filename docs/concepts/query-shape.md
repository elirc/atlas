# Concept: Query Shape ≠ Object Shape

The aggregate that is right for a write is wrong for a report. This is not a failure of the model —
it is two different jobs that happen to touch the same data, and pretending one shape serves both is
how you get slow reports and leaky writes at the same time.

## Two jobs, two shapes

**The write model** (aggregates: `Worker`, `PayrollRun`, `Engagement`) exists to protect INVARIANTS.
It loads whole, valid objects because a business rule needs the whole thing to be true: you cannot
activate an engagement without every checklist item, so `Activate()` needs the checklist in memory.
The shape is "one consistent thing, changed carefully."

**The read model** (projections: `HeadcountRow`, `EmploymentCostRow`) exists to answer a QUESTION.
"How many people, by country?" does not want a `Worker` with its terms history and probation rule — it
wants a `COUNT` grouped by a column. The shape is "exactly the columns this question needs, and not
one field more."

Forcing the write shape to serve reads is the classic mistake. It looks like reuse ("we already have
a `Worker` repository!") and it costs you an aggregate load per row.

## The tell: N+1

Here is the report that a `Worker` repository tempts you to write — the org compliance rollup:

```csharp
var workers = await workers.ListForOrg(orgId);          // 1 query
foreach (var w in workers)
    var docs = await documents.ListForWorker(w.Id);     // N queries — one per worker
```

Correct. Readable. `1 + N` round-trips that grow with every hire. It is fast in the demo (three
workers) and a fire in production (three thousand). The shape is the bug: you asked a per-worker
question N times instead of asking the org-wide question once.

The fix is not a faster loop. It is a different query:

```csharp
var workers  = await db.Workers.Where(...).Select(minimal).ToList();     // 1
var allDocs  = await db.WorkerDocuments.Where(o => o.OrgId == org).ToList(); // 1
// group allDocs by worker in memory, fold each through the SAME domain evaluator
```

Two queries, whatever the worker count. The compliance LOGIC did not change — the same pure
`ComplianceEvaluator` runs. Only the SHAPE of the read changed.

## Measure before you optimise

"It felt slow so I added `.Include`" is not engineering. Turn on query logging (here: a
`DbCommandInterceptor` that counts commands), get the number, then change the shape and get the number
again. A perf commit body without a before/after is a vibe. Ours says `N+1 → 2`, and a test asserts
the `2` so it stays `2`.

The subtlety: the test asserts the COUNT of queries, not the DURATION. Duration is flaky and
machine-dependent; the round-trip count is deterministic and is the actual cause of the slowness. Test
the cause, not the symptom.

## Where the line is: don't over-rotate into CQRS

Splitting the read path from the write path is cheap and worth it: a second port, result records,
done. Full CQRS — a message bus, a denormalised read store, eventual consistency — buys independent
scaling this system has no load to justify, and its tax (two models to keep in sync, a staleness
window on every read) is real. We took the half that pays: `IReportQueries` beside the repositories,
same database, no bus. The rule for graduating further is the same rule as always — the rule of three,
and a measured need, not a diagram.

## The boundary that keeps reads honest

A read model is a VIEW for a human. The moment a report's number starts driving a DECISION inside the
system — a gate, a guard, a calculation someone acts on — it stops being a view and becomes domain
logic, and it must move to the domain where invariants and tests live. Raw-ish SQL is safe in a report
precisely because nothing branches on it. Cross that line and the schema-is-the-contract deal is off.

## The tell, restated

The next time a report is slow, don't reach for a cache first. Ask: how many times did it hit the
database, and is that number a function of the data size? If it is, the shape is wrong, and a cache
would just make the wrong shape fast until the cache is stale. Fix the shape. Cache only what is
genuinely stable, and write its invalidation down before you add it.
