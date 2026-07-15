# Temporal Data: Three Problems That Look Like One

**One-pager · Sprint 03 · pairs with ADR-0004**

Teams say "we need history" and mean three different things. Untangling them is the sprint's
core skill:

| Problem | Question it answers | Atlas's tool |
|---|---|---|
| **Effective dating** | What terms APPLY on date X? | `EmploymentRecord.EffectiveFrom` + `Resolve(history, asOf)` |
| **Audit trail** | Who CHANGED what, when, and to what values? | `AuditEntry`, same transaction as the change |
| **Soft delete** | Can we un-remove this thing? | (not needed yet — and often a smell) |

Conflate them and you get the classic mess: an audit table doing double duty as a data store,
"deleted_at" columns filtering every query, and nobody able to answer the payroll question.

## The two clocks

Effective dating runs on **business time** (`DateOnly` — "the raise applies from Sept 1", true
in every timezone). The audit trail runs on **system time** (`DateTimeOffset` UTC — "the row was
written at 14:03:07Z"). A raise entered today, effective last month, has a business time in the
past and a system time of now — *both are correct*, and storing either in the other's column is
how "what did we pay in March?" becomes unanswerable.

(The third clock — transaction-time-aware "what did the system believe on date X?" — is full
bitemporal modeling. ADR-0004 records why we deliberately stop before it.)

## Append-only as a design stance

The update endpoint for terms performs an INSERT. That inversion is the whole trick:

- History cannot be lost, because losing it would require an UPDATE we never wrote.
- Retroactive changes are ordinary appends with a past effective date.
- Concurrency narrows to one constraint: one record per worker per effective day —
  enforced twice (domain check for the friendly error, unique index for the truth).

## The senior habit

When a schema review shows a mutable column holding something businesses renegotiate over time
(salary, price, rate, tier, address), ask: *who will ask what this value WAS?* If anyone —
payroll, finance, a regulator, a court — the column is actually a ledger, and it's cheaper to
admit that on day one.

## Try it

1. Give Maria a retroactive raise (`effectiveFrom` last month) via `docs/http/workers.http`,
   then query `?asOf=` either side of it. Explain each answer.
2. Delete `ThenByDescending(CreatedAtUtc)` from `EmploymentTerms.Resolve` and find the test that
   fails. Why does the DB's unique index NOT make that tiebreak dead code?
3. Sketch the "same-day correction" workflow ADR-0004 defers. What must it write so that both
   the correction AND the mistake remain visible?
