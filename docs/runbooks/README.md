# Runbooks

Concrete procedures for operating Atlas. A runbook is a checklist you can follow at 03:00 with a
pager going off — not a design doc. Every one here references the actual seams the course built
(outbox, idempotency, ACLs, effective-dated vendor assignment), and where a runbook claims a command
works, it is because someone ran it.

> 🔍 **A runbook you have not executed is fiction.** `backup-restore.md` carries a real restore
> transcript for exactly this reason. When you run one of these and it lies, fix the runbook in the
> same PR — a stale runbook is worse than none, because it is trusted.

## Index

| Runbook | When you reach for it |
|---|---|
| [deploy.md](deploy.md) | Shipping a release; rolling back one |
| [backup-restore.md](backup-restore.md) | Taking a backup; restoring one (with transcript) |
| [outbox-dispatcher-lag.md](outbox-dispatcher-lag.md) | `/health/ready` shows the outbox `degraded`; events are late |
| [webhook-replay.md](webhook-replay.md) | A partner missed deliveries; a failed delivery needs redriving |
| [payment-vendor-down.md](payment-vendor-down.md) | A payment vendor is 503-storming; `/pay` is failing |
| [rotate-secrets.md](rotate-secrets.md) | Rotating a partner API key or a webhook signing secret |
| [payroll-correction.md](payroll-correction.md) | A payroll run needs re-execution or correction |
| [incident-response.md](incident-response.md) | The severity ladder, comms, and how a postmortem starts |

## Severity ladder (used by every runbook)

| Sev | Meaning | Examples | Response |
|---|---|---|---|
| **SEV1** | Money is wrong, or the platform is down | double-charge, DB unreachable fleet-wide | page on-call immediately; incident channel; postmortem required |
| **SEV2** | A core flow is degraded | payment vendor 503-storm, dispatcher stalled during a payroll window | page on-call; postmortem required if customer-visible |
| **SEV3** | Contained / self-healing | one partner endpoint failing, single-node blip | ticket; fix in hours; no postmortem unless it recurs |

## The three health signals

- `GET /health/live` — process alive (orchestrator restart signal). Never gates on a dependency.
- `GET /health/ready` — should this node take traffic (load-balancer drain signal). Gates on the
  **database**; **reports** outbox lag as `degraded` without gating.
- Structured logs — every request line carries `CorrelationId`; metrics are log lines
  (`metric http.server.request.duration=...`). Aggregate by those.

## On-call conventions

- Confirm the signal before acting: a red dashboard is a hypothesis, `/health/ready` and the logs are
  evidence.
- Prefer the **least destructive** step that restores service. Draining a node is reversible;
  re-running payroll is not, so it has its own runbook and a second pair of eyes.
- Every money-moving action you take by hand uses an **Idempotency-Key** (S10) — so your own retry is
  safe.
- Write the timeline **as you go**. The postmortem is easier when the log already exists.
