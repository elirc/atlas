# Sprint 08 — Expenses & the Extracted Approval Component (The Reuse Sprint)

**Goal:** the second consumer of "approval" exists — so NOW we extract the abstraction. Reuse by
extraction, demonstrated as a refactor story in commits.

## Scope

**In:** ExpenseClaim (amount Money, category, receipt metadata, worker-submitted) with lifecycle;
**extraction:** S6's concrete leave-approval flow and the new expense flow both rewired through a
generic `ApprovalWorkflow<T>` domain component (states, approver-role resolution, decision record,
activity integration); expense endpoints; approval inbox endpoint (pending items for an
approver across types); `docs/http/expenses.http`.

**Out:** receipts as binary uploads (metadata only — storage is out of course scope), multi-step
approval chains (S9 invoices add a two-step variant), per-diem policy engines.

## Teaching focus

**The rule of three, honestly applied at two:** we extract when the second consumer *proves the
shape*, and we document what we deliberately did NOT parameterize. Refactoring as commit
narrative: behavior-preserving moves separated from behavior changes.

## Commit narrative (sketch)

1. `feat(domain): ExpenseClaim — concrete approval, copy-paste of S6 shape` (yes, deliberately)
2. `test: characterization tests pinning BOTH flows before refactor`
3. `refactor(domain): extract ApprovalWorkflow — leave rewired, zero behavior change` (tests untouched, still green — that's the proof)
4. `refactor(domain): expenses rewired through ApprovalWorkflow`
5. `feat(application+api): approval inbox across types`
6. `docs: ADR-0009 extraction over prediction, concepts/rule-of-three.md`

## Teaching placements

- 📘 on commit 1: "we copy-pasted on purpose — premature abstraction is more expensive than
  duplication, and today's duplication is tomorrow's specification."
- 📘 on characterization tests: refactoring's safety net; written BEFORE moving anything.
- 🔍 on commit 3's diff: "a true refactor commit: test files appear in the diff zero times."
- ⚠️ on `ApprovalWorkflow<T>`: what we did NOT abstract (escalation, delegation, N-step chains)
  and the YAGNI ledger of things we'll pay for only when a consumer demands them.
- 🔗 back to S6 debate (the Reviewer predicted this extraction; link it).

## Debate

`[Reviewer]`: "Your `ApprovalWorkflow<T>` will grow type parameters until it's a framework.
Approval is a *process*, so model it as data (workflow table) not generics." `[Author]`: a
workflow table is a workflow *engine* — the most expensive way to avoid writing two classes.
Generics stay until a third consumer wants runtime-configured steps; the moment we need a
designer-editable process, we buy, not build. Conclusion: generics, plus a tripwire comment in
the code marking the buy/build line. Reviewer signs off "under protest, tripwire noted."

## Exercises

- Add approval for employment-term changes (S3) as a third consumer. Time yourself; count files.
- Break a characterization test by "improving" a status name during refactor. Lesson?
- Argue the Reviewer's side: sketch the workflow-table schema they wanted. Where does it win?
