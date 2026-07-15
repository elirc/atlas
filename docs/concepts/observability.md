# Concept: Observability as a Data-Modeling Problem

The one-page version of what S15 wired, and — more usefully — how to *think* about operating a
service you built.

## Health checks answer two different questions

| Probe | Question | A failure means | Checks |
|---|---|---|---|
| `/health/live` | Is the **process** alive? | **Restart** me | Nothing downstream |
| `/health/ready` | Should **this node** get traffic? | **Drain** me (no restart) | Database (hard gate) |

Get these backwards and you manufacture outages:

- Liveness that pings the DB → one DB blip restarts *every* pod at once.
- Readiness that gates on the outbox dispatcher's lag → a background-job slowdown pulls every healthy
  node out of the load balancer and turns a lag into a front-door 503 storm.

So Atlas **reports** outbox lag on `/health/ready` (status `degraded`, with the numbers) but gates
only on the database. Lag pages an operator; it does not remove capacity. That distinction is the
whole lesson.

## Liveness vs readiness vs self-check

Three probes, three questions, three audiences:

- **Self-check** (startup): "should I *exist*?" — asked once, at boot; a failure fails the deploy.
- **Liveness** (running): "am I *alive*?" — asked forever; a failure restarts the pod.
- **Readiness** (running): "should I get *traffic*?" — asked forever; a failure drains the pod.

Same `CanConnect` query can back all three; the difference is the **action each failure triggers**.

## Correlation: a join key you carry on purpose

A correlation id is not magic. It is a column you would add to a table so rows relate — except the
"table" is your logs and the "rows" are log lines from one request. Assign it at the edge, put it in
a **logging scope** so every line inherits it, echo it to the caller, and now "show me everything
that happened for request X" is a `grep`, not an archaeology dig.

The honest state after S15: the id spans the **HTTP request**. Threading it onto the outbox row and
the webhook delivery — so one id follows a business event all the way to a partner's endpoint — is a
column + migration away and is named as the next increment. Naming the gap is the senior move;
pretending it is done is the junior one.

Security footnote: an inbound correlation id is **attacker-controlled input**. Bound its length and
strip control characters before it touches a log line — otherwise it is a log-forging vector. Trust
it to correlate; never trust it as data.

## Metrics you can afford today

You do not need an APM vendor to ask the two questions that matter first: *what is slow?* and *what is
backing up?* A **structured log line per measurement** answers both, aggregated by a log pipeline you
already run. Atlas records exactly two things — request duration and dispatcher lag — behind a
one-method `IMetrics` port. When log-derived aggregation stops answering the paging questions, swap
the adapter. Buying the dashboard before you know the question is how teams end up with a thousand
graphs and no answers.

## The rules, condensed

1. Liveness checks nothing; readiness gates on what you cannot serve without; never gate on what you
   can serve *around*.
2. A correlation id is a join key — assign at the edge, carry in a logging scope, sanitize on inbound.
3. Measure the two questions that would page you before buying tools to measure a hundred that would not.
4. A runbook you have not executed is fiction (see the restore transcript in `docs/runbooks/`).
