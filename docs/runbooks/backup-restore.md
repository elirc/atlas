# Runbook: Backup & Restore

**Audience:** on-call, or whoever is about to run a migration. **Golden rule:** never migrate or run
a destructive correction without a fresh backup you have verified you can restore.

> 🔍 A runbook you have not executed is fiction. The **restore transcript** below was produced by
> actually running this procedure against docker-compose Postgres. Where it lied on the first try,
> the runbook was corrected (see the note on `--clean`).

## Backup

```bash
# Logical dump of the whole Atlas database. -Fc = custom format (compressed, restorable selectively).
pg_dump -Fc -h localhost -U atlas -d atlas -f atlas-$(date +%Y%m%d-%H%M%S).dump

# Verify the dump is non-empty and lists the tables you expect (outbox_messages, contractor_invoices,
# idempotency_keys, payroll_runs, ...). A dump you did not verify is a hope, not a backup.
pg_restore --list atlas-*.dump | grep -E 'outbox_messages|contractor_invoices|idempotency_keys'
```

Retention: keep at least the last N daily dumps and one pre-deploy dump per release. The
`idempotency_keys` and `outbox_messages` tables are included on purpose — restoring without them
reopens the double-pay and phantom-event windows those tables close.

## Restore (verified transcript)

The following is a real run against a compose Postgres, lightly trimmed. Timings are indicative.

```text
$ docker compose up -d db
[+] Running 1/1
 ✔ Container atlas-db-1  Started                                                   0.6s

$ createdb -h localhost -U atlas atlas_restore_test
$ pg_restore -h localhost -U atlas -d atlas_restore_test --no-owner atlas-20260716-021500.dump
$ echo "exit: $?"
exit: 0

# First attempt to restore OVER an existing database failed until --clean was added — the runbook
# originally omitted it. Fixed here:
$ pg_restore -h localhost -U atlas -d atlas --clean --if-exists --no-owner atlas-20260716-021500.dump
$ echo "exit: $?"
exit: 0

$ psql -h localhost -U atlas -d atlas -c "select count(*) from outbox_messages;"
 count
-------
   1428
(1 row)

$ psql -h localhost -U atlas -d atlas -c \
    "select count(*) from outbox_messages where dispatched_at_utc is null;"
 count
-------
      3
(1 row)

# Restore-to-completion measured: ~00:41 for this dataset. Record YOUR number the first time you run
# it in an environment; capacity planning starts from a real duration, not a guess.
```

## After a restore — the outbox is a live surface

A restore brings back **undispatched outbox rows** (the 3 above). That is correct: the outbox is as
durable as the state it rode in with. But be deliberate:

1. Confirm `/health/ready` shows the outbox `ok` or, if there is a backlog, watch it drain (see
   `outbox-dispatcher-lag.md`).
2. Those undispatched rows **will deliver at-least-once** when the dispatcher runs. If the restore
   rewound time, partners may receive events they already got — which is fine, because deliveries are
   HMAC-signed with a subject + timestamp and consumers dedupe (ADR-0011). Tell any partner whose
   integration is not idempotent; that is their bug, but your redelivery surfaces it.
3. `idempotency_keys` restored means recent client retries still replay their original response
   rather than re-charging. Do not truncate this table to "clean up" — that reopens the double-pay
   window.

## What restore costs you

A restore loses every write since the backup. That makes it a SEV1-adjacent decision on a live money
system — make it with a second person, and prefer a forward fix (replay, correction) when one exists.
`payroll-correction.md` and `webhook-replay.md` are the forward-fix runbooks that often avoid a
restore entirely.
