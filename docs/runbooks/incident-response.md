# Runbook: Incident Response

The meta-runbook: how to run an incident, and how a postmortem starts. The specific playbooks
(`outbox-dispatcher-lag.md`, `payment-vendor-down.md`, ...) are the *what*; this is the *how*.

## The first five minutes

1. **Acknowledge the page.** One person owns the incident (the Incident Commander) — even if that
   person is also the only responder.
2. **Assign a severity** (see the ladder in `README.md`). When unsure, round up; you can always
   downgrade.
3. **Open a timeline.** A running text log with timestamps. Write as you go — memory is not evidence,
   and the postmortem is built from this.
4. **Confirm the signal.** `/health/live`, `/health/ready`, and the logs (grep the `CorrelationId`
   from a failing request). A dashboard is a hypothesis; these are evidence.
5. **Stop the bleeding before finding the cause.** Draining a bad node, pausing a hammering retrier,
   or failing a deploy is reversible. Root-causing can wait until customers are safe.

## Following one request through the system

The correlation id makes this a `grep`, not an investigation:

1. Get the id from the failing response header `X-Correlation-Id` (or from the client).
2. `grep` the logs for that id — every line the request emitted carries it (the logging scope).
3. That gives you the request, the use case, and the outbox rows it enqueued. When the
   request→outbox→webhook correlation lands (named in ADR-0018 as the next increment), the same id
   will reach the partner delivery too.

## Reversible-first toolkit

| Lever | Reversible? | Use when |
|---|---|---|
| Drain a node (`/health/ready` fails → LB pulls it) | yes | one node is sick |
| Roll back the binary (additive migration) | yes | a bad deploy |
| Pause a webhook subscription / retrier | yes | you are the retry storm |
| Un-park / redeliver outbox rows | yes | after fixing a delivery cause |
| Re-execute a payroll run (idempotent) | yes (no-op) | a run's completion is uncertain |
| Restore from backup | **no** (loses writes) | last resort; second person required |
| Edit executed payslips / delete outbox rows | **never** | — |

## Blameless, but not causeless

When the fire is out, write the postmortem (see the two examples in `docs/postmortems/`). The rules:

- **Blameless.** "A person made a mistake" is never a root cause; the system that let a mistake become
  an incident is. If the answer is "Priya should have been more careful", keep asking why the system
  required Priya to be careful.
- **But not causeless.** "Things happen" is not a postmortem. Name the **one root cause** and the
  **contributing causes** (usually 3–6) — the difference is the teaching point (see the outbox-stall
  postmortem's 📘). A postmortem with no cause produces no action items.
- **Action items are commits, not wishes.** At least one action item from every real incident should
  land as a code or runbook change with an owner and a due date. The outbox-stall drill's AI-1/AI-2
  became commits in this very PR; that is the bar.

## When to write one

- Every SEV1 and SEV2. Every SEV3 that **recurred**. A near-miss that would have been a SEV1 if one
  more thing had gone wrong (the double-pay window is exactly this — caught before prod, written up
  anyway, because the *lesson* is the deliverable).
