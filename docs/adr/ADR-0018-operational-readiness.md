# ADR-0018: Operational Readiness — Health, Correlation, and Log-Based Metrics

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 15)
- **Deciders:** [Author], [Reviewer]

> Numbering note: this ADR assumes S12–S14 landed ADR-0014 (vendor abstraction), ADR-0015 (API
> versioning), ADR-0016 (security posture), and ADR-0017 (read models & the cache ledger). If those
> settle on different numbers at integration, renumber this to the next free slot.

## Context

Fifteen sprints built the thing. This one is about *operating* the thing without buying an
observability platform we cannot run in CI. Three questions had to get concrete answers, each of
which is a design decision, not a config value:

1. How does an orchestrator know when to **restart** a node versus **drain** it?
2. When one webhook delivery misbehaves at 03:00, how do we follow **one business event** from the
   HTTP request that caused it, through the outbox row, to the delivery — using only logs?
3. What do we **measure**, and where does that measurement live so that swapping in real metrics
   infrastructure later is a local diff, not a rewrite?

## Decision 1: liveness and readiness are different questions, and gating them wrong causes outages

- `/health/live` — **liveness**. Is the process alive? Checks **nothing** downstream. A failed
  liveness check is a *restart* signal. If liveness pinged the database, a single DB blip would
  restart every pod simultaneously — the outage you were trying to prevent.
- `/health/ready` — **readiness**. Should *this* node receive traffic? A failed readiness check
  *drains* the node from the load balancer (no restart). It is gated on the **database only**: with
  no database there is nothing to serve, so 503.

The outbox dispatcher's **lag is reported** in the readiness body but **does not gate** readiness.
This is the load-bearing subtlety. A lagging dispatcher does not stop the API from serving HTTP;
gating readiness on lag would pull healthy nodes out of rotation and *cascade* a background-job
problem into a front-door outage. Lag is an **alert** (it pages an operator via the "degraded"
status and the runbook), not a **gate**.

- **Rejected: gate readiness on outbox lag.** It reads as rigorous and is dangerous — it couples the
  serving tier's availability to the dispatcher's throughput, exactly the coupling the outbox was
  built to avoid.
- **Rejected: a single `/health` that means both.** Orchestrators use the two probes for opposite
  actions; merging them forces one action for two questions.

## Decision 2: a correlation id at the edge, in a logging scope

`RequestTelemetryMiddleware` assigns a correlation id (or accepts and **sanitizes** an inbound one —
an untrusted string, bounded and stripped of control characters to prevent log forging), opens a
logging scope so every line the request emits carries it, and echoes it on the response.

Observability is a data-modeling problem in an ops costume: the correlation id is just a **join key**
you carry so one request's logs can be grouped. S15 ships the **request half**. Threading the same id
onto the `OutboxMessage` row and the webhook delivery — so one id spans request → event → webhook —
needs a column and a migration; it is **named as the next increment** here and in the retrospective's
open-debts, not shipped, because S15's mandate is to operate what exists, not to grow it.

## Decision 3: metrics are structured log lines, behind a one-file port

`IMetrics` (Application) has one method; `LoggingMetrics` (Infrastructure) writes each metric as a
structured log line. No APM SDK, no exporter, no backend in CI. Request durations, and the
dispatcher lag surfaced by `/health/ready`, are the two things we actually watch.

- **The Reviewer's objection:** "Log-based metrics won't survive real traffic; wire OpenTelemetry."
- **Resolution:** agreed on the destination, disputed on the *when*. OTel without a backend is
  ceremony; log-derived metrics teach the **questions** (what would page us?) before buying the
  answers. Because the seam is one file behind a port, swapping `LoggingMetrics` for an OTel adapter
  is an **S12-grade local diff** — now a checkable claim, not a promise. The migration trigger:
  buy metrics infrastructure when log-derived aggregation stops answering the paging questions
  (cardinality, retention, or query latency makes it lie).

## Decision 4: a fail-fast startup self-check

Before serving, `StartupSelfCheck` verifies the database is reachable and **crashes the process** if
not. A misconfigured connection string should fail the *deploy* (non-zero exit, loud in the logs),
not degrade into 500s while a green liveness probe reports "fine". Same query as readiness, different
question: readiness asks a running node "should I get traffic?" repeatedly; the self-check asks a
starting node "should I exist?" once.

## Consequences

- Orchestrators get the two signals they need with the two meanings they expect; the cascade failure
  mode is designed out, not commented against.
- One correlation id per request threads the logs; the outbox/webhook extension is a recorded, small
  next step rather than an accidental omission.
- Metrics have a home and a swap path. The last debate of the course ends the way the first did —
  with the diff as the argument.
- These endpoints are **excluded from the OpenAPI document** on purpose: they are for operators, not
  partners, so they never enter the contract or the drift test.
