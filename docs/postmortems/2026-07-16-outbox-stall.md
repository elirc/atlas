# Postmortem: Outbox Dispatcher Stall During a Payroll Window

- **Date of incident:** 2026-07-16 (game-day drill, run against staging)
- **Authors:** [Author] (Incident Commander), [Reviewer]
- **Status:** Resolved; action items AI-1, AI-2 shipped in this PR; AI-3 deferred with an owner
- **Severity:** SEV2 (core flow degraded during a payroll window; no money lost, events delayed)

> This is a **rehearsed** disaster. We deliberately stalled the outbox dispatcher during a simulated
> payroll window to find out what we would actually see, do, and miss. It is written up exactly as a
> real incident would be, because the point of a drill is the postmortem. Blameless, but not causeless.

## Summary

During a Friday payroll window we injected a fault that stalled the outbox dispatcher for 22 minutes.
Payroll executed correctly and no money was wrong — but the `payroll.run.executed` and `invoice.paid`
events that partners depend on sat undelivered in `outbox_messages`, and **nothing paged us**. We
discovered the stall only when a (simulated) partner asked why their run-completion webhook was late.
The events all delivered, at-least-once and intact, once the dispatcher recovered.

## Impact

- ~140 events delayed up to 22 minutes; peak `pending` ~980, peak `oldestDueAgeSeconds` ~1,300.
- Zero events lost, zero duplicated at the business layer (the outbox is as durable as the state; the
  restart lost nothing).
- Zero incorrect payments — payroll's correctness is independent of event delivery.
- One customer-visible symptom: late webhooks. In a real incident this is a trust hit, not a money
  hit — but for a partner whose downstream automation gates on our webhook, "late" can mean "their
  contractors are paid late by their system."

## Timeline (UTC)

| Time | Event |
|---|---|
| 14:00 | Payroll window opens; runs begin executing. Events enqueue to the outbox normally. |
| 14:03 | **Fault injected:** a delivery adapter misconfiguration makes every webhook delivery throw. |
| 14:03–14:11 | Failed deliveries back off and retry (correct behavior). `pending` climbs; the *due* age stays low because backed-off rows are not yet due. No alert — there was no lag alert. |
| 14:12 | Backoff windows elapse; rows become due again and keep failing. `oldestDueAgeSeconds` starts climbing past 120s. Still no page. |
| 14:19 | Simulated partner opens a ticket: "run-completion webhook 15 min late." |
| 14:21 | IC pulls up `/health/ready`: `outbox-dispatcher` reads `degraded, pending=810, oldestDueAgeSeconds=520, poisoned=0`. **First objective confirmation** — and it came from a human checking, not an alert. |
| 14:23 | Root cause found in logs (`Outbox delivery failed ... attempt N`, same adapter every time). Fault removed (adapter config corrected). |
| 14:24 | Rolling restart of the API to guarantee a clean dispatcher; outbox rows untouched. |
| 14:25–14:26 | Dispatcher drains the backlog (batch of 100, loop-while-full). `pending` → 0, `oldestDueAgeSeconds` → 0. |
| 14:26 | Partner confirms deliveries received (deduped on subject+timestamp; some were redelivered). Incident closed. |

## Root cause vs contributing causes

> 📘 The difference between "root cause" and the contributing causes is the whole discipline. There is
> one root cause; there are several things that turned a small fault into a 22-minute customer-visible
> delay. Fixing only the root cause leaves the amplifiers in place for next time.

**Root cause:** a delivery-adapter misconfiguration made every webhook delivery throw. (In the drill,
injected; in the wild, this is a bad secret, a DNS failure to every partner, or a delivery-code
regression.)

**Contributing causes** (the amplifiers):

1. **No lag alert.** The dispatcher's health was observable only if a human went looking. The system
   was *resilient* (it never lost an event) but *invisible*. This, not the root cause, is why it took
   16 minutes to notice. — **AI-1, AI-2**
2. **Detection came from a customer, not a signal.** The first alert was a support ticket. Any
   incident you learn about from a customer is an observability gap by definition.
3. **Backoff masked the early climb.** Because backed-off rows are (correctly) not counted as lag, the
   first eight minutes looked calmer than they were. The right metric (`oldestDueAgeSeconds` of *due*
   rows) is subtle, and we had not graphed it. — **AI-1**
4. **Single in-process dispatcher.** One stalled process stalled all delivery. This is the accepted
   S10 trade-off (ADR-0011), fine at our scale — but the drill confirms the migration trigger is real
   and should be watched. — **AI-3**
5. **Runbook gap.** The responder reconstructed the "un-park poison / confirm drain" steps live.
   Correct, but slow. — **AI-2**

## What went right (keep doing)

- The outbox did its job: **at-least-once, nothing lost, nothing double-processed** at the business
  layer. The restart-to-recover play was trivial precisely because the table is the durability, not
  the process.
- Payroll correctness was completely decoupled from event delivery — money was never at risk.
- The `degraded` status on `/health/ready` gave an instant, objective read once someone looked.

## Action items

| ID | Action | Owner | Status |
|---|---|---|---|
| **AI-1** | Surface outbox depth + `oldestDueAgeSeconds` + poison count on `/health/ready` as a `degraded` signal (make the invisible visible). | [Author] | ✅ Shipped this PR (`AtlasReadinessProbe`) |
| **AI-2** | Write `docs/runbooks/outbox-dispatcher-lag.md` (triage, un-park poison, confirm drain) and define the alert thresholds (`oldestDueAgeSeconds > 120` sustained; `poisoned > 0`). | [Author] | ✅ Shipped this PR (runbook + thresholds) |
| **AI-3** | Evaluate the ADR-0011 migration trigger (LISTEN/NOTIFY or a broker for multi-node) now that the single-dispatcher amplifier is demonstrated. | [Reviewer] | ⏳ Deferred, owner assigned |

> Note on AI-1/AI-2: the S15 playbook sketched these as "dispatcher stall guards." The drill showed the
> dispatcher's **resilience** was already correct (drain-level failures are caught, logged, and
> retried; poison is capped) — the actual gap was **observability**, so the guards we shipped are the
> lag signal + the alert + the runbook, not changes to the drain loop. Naming what the fix really was
> beats shipping a fix to a problem we did not have.

## Lessons

1. A system can be perfectly correct and still fail its users by being **invisible**. Resilience
   without observability is a silent outage waiting for a customer to report it.
2. The subtle metric is the right one. "Depth" would have alerted at 14:03 and cried wolf on every
   backoff; "oldest *due* age" alerts on a real stall. Measure the thing that means what you think.
3. Run the drill. We found the alert gap in a game day instead of at 02:00 on a real payroll Friday.
