# Sprint 03 — Workers, Effective-Dated Employment Records, Audit Log

**Goal:** CRUD done excellently. The worker aggregate proves that "just a CRUD screen" hides the
two hardest ideas in business software: time and history.

## Scope

**In:** Worker (profile, country, contact); EmploymentRecord — **effective-dated** terms (salary,
title, weekly hours): changes append a record with `EffectiveFrom`, never mutate history;
"terms as of a date" resolution; probation-end computation from country notice/probation rules
(v1 data); append-only AuditLog (who/what/when/before/after) written in the same transaction;
worker endpoints incl. `GET .../terms?asOf=`; `docs/http/workers.http`.

**Out:** documents/compliance (S11), payroll consumption of terms (S7).

## Teaching focus

Temporal modeling: effective dating vs audit trail vs soft delete (three different problems).
Append-only as a design stance. `DateOnly` vs `DateTime` and why HR dates are calendar dates.

## Commit narrative (sketch)

1. `feat(domain): Worker + EmploymentRecord with effective-date resolution`
2. `test(domain): terms-as-of property tests (records shuffled → same resolution)`
3. `feat(domain): probation window computation from country rules`  ← **ledger #2 planted here**
4. `feat(application): hire/update-terms/get-as-of use cases + audit port`
5. `feat(infrastructure+api): persistence, endpoints, audit sink`
6. `docs: ADR-0004 effective dating, concepts/temporal-data.md`

## Teaching placements

- 📘 on EffectiveFrom resolution: bitemporal data, and why we deliberately stop at one time axis.
- 🔍 on the update-terms use case: "look for the missing UPDATE — appends only."
- ⚠️ on `DateOnly` in the API contract: serialization and time zones at the edge.
- 🔗 audit sink ← ADR-0003 tenancy (audit rows are org-scoped too).
- **Ledger #5 lands silently here** (worker GET-by-id repository method). The `[Reviewer]`
  comment on the *list* endpoint praises scoping again. Nothing on the by-id path.
- **Ledger #2:** probation computation calls `DateTime.Now` internally on one branch. Commit body
  describes the feature honestly, no hint.

## Debate

`[Reviewer]`: "Effective dating is YAGNI — a `salary` column plus the audit log reconstructs
history." `[Author]`: reconstruction from audit is a query-time archaeology project; payroll (S7)
needs "terms as of cycle date" as a first-class read. Reviewer partially wins: we drop
`EffectiveTo` (derivable) and the sprint gets simpler. Follow-up commit does it.

## Exercises

- Add a retroactive raise (EffectiveFrom in the past) via the API. What does `asOf` return for
  the intervening dates? Why is that correct?
- Delete the property test and try to hand-write cases that give the same confidence. Report back.
- The audit row and the employment record are written in one transaction. List two failure modes
  if they weren't.
