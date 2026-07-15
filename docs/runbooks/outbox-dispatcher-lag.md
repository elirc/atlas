# Runbook: Outbox Dispatcher Is Lagging

**Signal:** `/health/ready` shows the `outbox-dispatcher` check `degraded`, or partners report events
arriving late, or `oldestDueAgeSeconds` is climbing. **Severity:** SEV3 normally; **SEV2 during a
payroll window** (late `payroll.run.executed` events delay downstream integrations).

> 📘 This is the operational signal ADR-0011 promised S15 would watch: "the outbox table's depth and
> oldest-undispatched age are the health signals the runbook watches." Here is the watching.

## Understand the signal first

The readiness `detail` reads: `pending=<n>, oldestDueAgeSeconds=<s>, poisoned=<p>`.

- **pending** — undispatched rows. A number > 0 is normal in flight; a *growing* number is a backlog.
- **oldestDueAgeSeconds** — age of the oldest row that is **due** (not backed off to the future).
  This is the real lag. Rows backed off after a failed delivery do **not** count — that is by design,
  so one broken partner endpoint does not read as a platform stall.
- **poisoned** — rows that hit `MaxAttempts` (12) and are parked a century out. These are stuck until
  a human intervenes; they never "catch up" on their own.

## Triage

1. **Is the dispatcher running at all?** It is a hosted service inside the API. Check the API logs for
   `Outbox dispatcher started` at boot and the absence of repeated `Outbox drain iteration failed`.
   A drain that throws every tick (e.g. the DB is degraded) logs an error and keeps polling — pending
   climbs, oldestDueAgeSeconds climbs.
   - **If the DB is the cause:** the database readiness check is likely also unhappy. Fix the DB;
     the dispatcher recovers on the next tick with no further action (at-least-once means nothing was
     lost).

2. **Is it a poison pile-up?** If `poisoned` > 0 and pending is otherwise flat, the backlog is not
   throughput — it is a specific event type failing every delivery. Find it:

   ```sql
   select type, count(*), max(attempts), max(left(last_error, 200)) as sample_error
   from outbox_messages
   where dispatched_at_utc is null and attempts >= 12
   group by type order by count(*) desc;
   ```

   Fix the cause (a bad webhook target, a delivery adapter bug), then **un-park** the rows so the next
   drain retries them:

   ```sql
   -- Make parked rows due again. Do this only AFTER the cause is fixed, or they re-poison.
   update outbox_messages
   set next_attempt_at_utc = now()
   where dispatched_at_utc is null and attempts >= 12 and type = '<the-failing-type>';
   ```

3. **Is it just volume?** If pending is large but oldestDueAgeSeconds is small and shrinking, the
   dispatcher is keeping up on a spike — do nothing, watch it drain. Each drain clears a batch of 100
   and loops immediately while full, so a backlog clears fast.

## If it is genuinely stalled and you need to force a drain

The dispatcher polls every 5s; you rarely need to force it. If you must (e.g. right after fixing the
DB, to confirm recovery), the safe lever is a rolling restart of the API — a fresh process starts a
fresh dispatcher, and the outbox rows are exactly where they were. **Do not** delete or hand-mark
rows dispatched to "clear" a backlog: that is a lost event, the exact failure the outbox exists to
prevent.

## Do NOT drain readiness on lag

Tempting during an incident: "the outbox is behind, mark the node not-ready." **No.** Readiness gates
on the DB, not the dispatcher, precisely so a background lag does not pull serving nodes and cascade
(ADR-0018). Lag is a page, not a gate. If you find yourself wanting to gate on it, you want an alert
threshold, which the `degraded` status already gives you.

## Prevention / follow-ups

- Alert on `oldestDueAgeSeconds > 120` (the probe's warn threshold) sustained for N minutes, and on
  `poisoned > 0` at any level.
- Recorded migration trigger (ADR-0011): when polling latency or multi-node duplicate-drain contention
  becomes real, move the drain to `LISTEN/NOTIFY` or a broker. The outbox **table** survives that
  move unchanged — that is the point of the port being the table.
