# Sprint 15 — Production Readiness → v1.0.0

**Goal:** operating what we built: health, logs, runbooks, one rehearsed disaster — and the
course's honest accounting: the retrospective and the **flaw-ledger reveal**.

## Scope

**In:** health endpoints (`/health/live`, `/health/ready` — readiness checks DB + outbox
dispatcher lag); structured logging (correlation ids per request → into outbox → into webhook
delivery: one id traces a business event end-to-end); operational metrics surface (request
timings, dispatcher depth, payroll run durations — log-based, no APM dependency); **runbooks**
(`docs/runbooks/`): deploy, backup/restore (with an actually-executed restore transcript),
webhook-replay, payroll-correction, incident-response; **the incident drill**: a simulated
outbox-dispatcher stall during a payroll window, worked end-to-end, written up as
`docs/postmortems/2026-XX-outbox-stall.md` (timeline, contributing causes, action items — two of
which land as commits in this PR); **the reveal**: `docs/sprints/flaw-ledger.md` committed —
all five flaws, plant/harvest commit links, and the review comments that looked past them;
**COURSE-RETROSPECTIVE.md**: sprint-by-sprint takeaways, the arcs, the thesis verdict (S11/S12
numbers), further-exercise syllabus; final README architecture diagram (Mermaid hexagon);
**tag `v1.0.0` + GitHub Release**; close all milestones.

**Out:** real deployment targets, container orchestration, APM vendors.

## Teaching focus

Operations as a design review you can't argue with. Postmortems: blameless but not causeless.
Reading your own course critically — the retrospective models how seniors extract lessons.

## Commit narrative (sketch)

1. `feat(api): health endpoints + readiness checks`
2. `feat: correlation ids end-to-end (request → outbox → webhook delivery)`
3. `feat: operational metrics logging + dispatcher lag gauge`
4. `docs: runbooks (deploy, backup-restore w/ transcript, webhook-replay, payroll-correction, incident)`
5. `fix: outbox dispatcher stall guards` (postmortem action items AI-1, AI-2)
6. `docs: postmortem — the outbox stall drill`
7. `docs: flaw ledger revealed + COURSE-RETROSPECTIVE + final architecture diagram`

## Teaching placements

- 📘 on correlation ids: observability is a data-modeling problem wearing an ops costume.
- 📘 on the postmortem: the difference between "root cause" and the five contributing causes.
- 🔍 on runbooks: "a runbook you haven't executed is fiction — note the restore transcript."
- ⚠️ on readiness vs liveness: getting them backwards causes the outage you were preventing.
- 🔗 the reveal links every plant/harvest pair; the retrospective links every debate to its
  outcome (three Reviewer wins, tallied).

## Debate

`[Reviewer]`: "Log-based metrics won't survive real traffic; we should have wired OpenTelemetry."
`[Author]`: agreed on the destination, disputed on the when — OTel without a backend is
ceremony; log-derived metrics teach the *questions* (what would page us?) before buying the
answers. The interface (`IMetrics`) is one file; swapping it is an S12-grade local diff — which
is now a checkable claim, not a promise. Reviewer signs off: "the course's last debate ends the
way the first one did — with the diff as the argument."

## Post-merge

Tag `v1.0.0`, GitHub Release (full-course recap), close milestone 15, verify all 15 closed,
final README badge/sweep.

## Exercises

- Execute the backup/restore runbook yourself against docker-compose Postgres. Time it. Update
  the runbook where it lied.
- Trace one webhook delivery's correlation id backwards to the HTTP request that caused it, using
  only logs.
- Write sprint 16's playbook: what would YOU build next, and which existing seam absorbs it?
