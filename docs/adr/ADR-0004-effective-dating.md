# ADR-0004: Effective-Dated Employment Records + a Same-Transaction Audit Log

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 03)
- **Deciders:** [Author], [Reviewer]

## Context

Employment terms change: raises, promotions, hour reductions — sometimes retroactively, and in a
payroll product "what were the terms on date X?" is not a nice-to-have, it is Sprint 07's core
input. Separately, an EOR platform is an entity that acts on other people's employees; every
mutation needs an attributable record.

## Decision 1: terms are APPENDED, never updated

`EmploymentRecord` rows are immutable, each carrying `EffectiveFrom` (a `DateOnly` — HR dates
are calendar dates, not instants). "Current terms" is a *resolution*
(`EmploymentTerms.Resolve(history, asOf)`: latest `EffectiveFrom <= asOf` wins), not a column.

- **No `EffectiveTo`.** The Reviewer challenged effective dating as YAGNI; the debate landed in
  the middle: dating stays (payroll needs "as of" as a first-class read), but the closing-date
  column goes — a record ends where the next begins, and a derivable column that must be
  maintained under concurrent appends is a consistency bug waiting for its moment.
- One record per worker per effective day. The domain checks it for a friendly 422; the unique
  index `(WorkerId, EffectiveFrom)` makes it true under races. Same-day *corrections* are a
  deliberate future workflow (S15 runbook), not an accidental double-submit.
- Salary is `decimal` + ISO currency string **for exactly one more sprint** — Money (S4) is the
  scheduled promotion. The smell is intentional and dated, which is different from accidental.

## Decision 2: the audit log rides the same transaction

`AuditEntry` (who/what/when/details-JSON, append-only) is staged into the SAME unit of work as
the change it describes: `IAuditTrail.Append` is synchronous into the change tracker;
`IUnitOfWork.CommitAsync` makes the change and its audit row durable together. There is no
"audit failed, continue anyway" branch — best-effort audit teaches people to trust records that
can lie.

Ops actions are attributable through the same mechanism (ActorUserId), which is what makes the
S02 decision "platform staff pass role checks" defensible.

## Consequences

- Reads are folds: current terms cost a small in-memory resolution over the worker's history
  (bounded — humans don't get thousands of raises). If it ever hurts, a materialized "current"
  projection is an S14-style read-model, not a schema change.
- Retroactive raises Just Work: append with a past `EffectiveFrom`; historical queries after the
  append see the corrected truth; the audit log preserves that the correction happened.
- Payroll (S7) gets "terms as of cycle date" for free.

## Rejected alternatives

- **`salary` column + audit-log reconstruction:** history becomes query-time archaeology, and
  the audit log gains a second job (data store) it must never have.
- **`EffectiveFrom`/`EffectiveTo` pairs:** double bookkeeping; closing the previous record and
  opening the next must be atomic and race-free forever.
- **Full bitemporal (valid-time + transaction-time):** the right tool when regulators ask "what
  did the system BELIEVE on date X?" — a real EOR eventually needs it; we stop at one time axis
  and say so here, deliberately.
- **Event sourcing the worker aggregate:** the ledger-shaped subset we need (immutable records +
  resolution) without the projection/replay machinery we don't.
