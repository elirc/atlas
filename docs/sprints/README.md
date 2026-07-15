# Sprint Playbooks

Execution scripts for the 15-sprint build of **Atlas** (see [SPEC.md](../curriculum/SPEC.md)).
The course author writes all code, commits, PRs, teaching comments, and design-debate threads;
the learner **observes and studies** each merged PR in order.

**Start here:** [00-workflow.md](00-workflow.md) — the standard ritual every sprint follows.
Each sprint file specifies only what is unique to that sprint: the commit narrative, teaching
placements, planted debates, and post-merge steps.

## Phase 1 — MVP → `v0.5.0`

| Sprint | Playbook | Headline |
|--------|----------|----------|
| 01 | [sprint-01.md](sprint-01.md) | Walking skeleton: solution, arch tests, countries+orgs slice, OpenAPI, CI |
| 02 | [sprint-02.md](sprint-02.md) | AuthN/authZ, users, roles, multi-tenancy |
| 03 | [sprint-03.md](sprint-03.md) | Workers, effective-dated employment, audit log |
| 04 | [sprint-04.md](sprint-04.md) | Quotes & cost calculator: Money, FX port, rule packs v1 |
| 05 | [sprint-05.md](sprint-05.md) | Hiring lifecycle state machine → **v0.5.0** |

## Phase 2 — Full product → `v1.0.0`

| Sprint | Playbook | Headline |
|--------|----------|----------|
| 06 | [sprint-06.md](sprint-06.md) | Time off: accruals, proration, country minimums |
| 07 | [sprint-07.md](sprint-07.md) | Payroll engine: gross-to-net, cycles, idempotent execution |
| 08 | [sprint-08.md](sprint-08.md) | Expenses via the extracted Approval component (reuse sprint) |
| 09 | [sprint-09.md](sprint-09.md) | Contractor invoicing + mock payment provider (ACL) |
| 10 | [sprint-10.md](sprint-10.md) | Integration surface: webhooks, outbox, API keys, idempotency |
| 11 | [sprint-11.md](sprint-11.md) | Compliance + rule packs v2 as plugins (thesis proof) |
| 12 | [sprint-12.md](sprint-12.md) | **The pivot sprint**: second payroll vendor + API v2 (thesis exam) |
| 13 | [sprint-13.md](sprint-13.md) | Hardening: security audit, threat model |
| 14 | [sprint-14.md](sprint-14.md) | Reporting & performance: read models, N+1, caching |
| 15 | [sprint-15.md](sprint-15.md) | Production readiness, retrospective → **v1.0.0** |

## Cross-sprint arcs the learner should watch

- **Money** (S4 → S7 → S13) — value object, allocation, and what precision bugs look like.
- **The clock** (S3 → S6) — from convenient `DateTime.Now` to `IClock` + `DateOnly` semantics.
- **Approval** (S6 → S8 → S9) — an abstraction extracted on the second use, not predicted.
- **Events leaving the system** (S5 → S9 → S10) — activity rows → notifications → transactional outbox.
- **Rule packs** (S4 → S11 → S12) — data → plugins → the pivot exam.
- **The flaw ledger** — now revealed at [flaw-ledger.md](flaw-ledger.md): five flaws were planted in
  early sprints and harvested later with RED→GREEN arcs (#2 S06, #3 & #4 S10, #1 & #5 S13). 😉
