# Runbook: Deploy (and roll back)

**Audience:** whoever is shipping a release. **Prereq:** CI green on the commit being deployed.

## What a deploy is here

Atlas is a single ASP.NET Core process plus a Postgres database. There is no separate migration
worker and no separate dispatcher process — the outbox dispatcher is a hosted service **inside** the
API (ADR-0011). So "deploy" is: migrate the database, start the new binary, confirm it is ready.

## Pre-deploy checklist

- [ ] CI is green on the target commit (`build -warnaserror`, `test`, `format`, arch tests, OpenAPI
      drift, gitleaks, vulnerable-package audit).
- [ ] The release has a tag or you are deploying `main` at a known SHA (record it in the deploy log).
- [ ] Migrations in this release are **backward compatible** with the currently-running binary (see
      "Expand/contract" below). If not, this is a two-phase deploy.
- [ ] A fresh backup exists (see `backup-restore.md`) — never migrate without one.

## Procedure

```bash
# 1. Back up first (see backup-restore.md). Never skip on a migrating deploy.

# 2. Apply migrations. Idempotent: re-running is a no-op if already applied.
dotnet run --project src/Atlas.Api -- migrate

# 3. Start (or roll) the new binary. On the very first environment bring-up only:
dotnet run --project src/Atlas.Api -- migrate seed   # seed = reference data + ops account

# 4. Confirm readiness before sending traffic.
curl -fsS http://localhost:5000/health/ready | jq .
#    Expect "status":"ready" and database "ok". The StartupSelfCheck already refused to boot if the
#    DB were unreachable, so a process that is up has passed that bar.

# 5. Confirm the outbox is draining (not just reachable).
curl -fsS http://localhost:5000/health/ready | jq '.checks[] | select(.name=="outbox-dispatcher")'
#    "status":"ok" with oldestDueAgeSeconds near 0. If "degraded", see outbox-dispatcher-lag.md
#    BEFORE declaring the deploy done.
```

## Expand/contract (why most deploys are safe, and which are not)

The house rule (00-workflow): **no breaking change to a shipped v1 endpoint after S5**. Migrations
follow the same discipline — **expand** (add columns/tables, backfill, deploy code that writes both
old and new) in one release; **contract** (drop the old) only in a later release, after the old
binary is gone. A single-phase deploy is safe only when the migration is additive. If it is not,
split it; a rollback that hits a contracted schema is how a bad deploy becomes a SEV1.

## Rollback

1. If the new binary is bad but the migration was **additive** (the common case): redeploy the
   previous binary. The additive schema is compatible; no DB change needed.
2. If the migration was **not** additive: this is why you split it. Roll the binary back to the one
   that matches the pre-migration schema, and only then decide whether to restore (see
   `backup-restore.md`). Restoring loses writes since the backup — a SEV1-adjacent decision, made
   with a second person.
3. Either way: after rollback, re-check `/health/ready` and the outbox line. A rollback that leaves
   the dispatcher parked is a hidden second incident.

## After any deploy

- Watch request-duration metrics (`metric http.server.request.duration`) and the outbox lag for the
  first few minutes.
- Note the deployed SHA and the outcome in the deploy log. A deploy you cannot later place on a
  timeline is a postmortem you cannot write.
