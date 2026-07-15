# ADR-0009: Extraction over Prediction — the Approval Component

- **Status:** Accepted
- **Date:** 2026-07-15 (Sprint 08)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

S06 shipped a leave-approval flow — a status, a decider, a decision timestamp, guarded
transitions — and DELIBERATELY inlined it on `LeaveRequest`, with a breadcrumb saying the
abstraction would be extracted when a second consumer arrived. S08 is that arrival: expense claims
need the same lifecycle. Now there are two examples, so we can see which parts are shared and which
only look shared. This ADR records how we extracted, and — just as important — what we refused to
extract.

The thesis of the course is "software is optimized for change." The change here is "add a second
approvable thing," and the diff should be small. But the wrong abstraction makes every future
change *bigger*, so the discipline is to abstract only what two real consumers proved.

## Decision 1: extract on the SECOND consumer (the rule of three, honestly applied at two)

We did not build a generic approval component in S06 (one consumer is a sample size of one — every
"seam" is a guess). We did not wait for a third (invoices, S09) either. Two consumers is the point
where the shape is *proven* but the duplication is still cheap to reconcile: you can see, line by
line, which parts of leave and expense are identical and which diverge.

The extraction is told as a commit narrative so the method is legible:

1. `ExpenseClaim` is written as a deliberate **copy-paste** of the leave shape.
2. **Characterization tests** pin BOTH flows' current behaviour — including exact guard-message
   text — *before* anything moves.
3. `ApprovalWorkflow<T>` is extracted and leave is rewired through it, with **zero** change to the
   characterization tests. That untouched-and-green test file IS the proof the move was
   behaviour-preserving. *A refactor whose test files appear in the diff is not a refactor.*
4. Expenses are rewired through the same component; their characterization tests stay green too.

## Decision 2: generics, not a workflow TABLE (the buy-vs-build line)

`ApprovalWorkflow<T>` is a generic guard: the single type parameter `T` supplies the noun for
error messages ("leave request" vs "expense claim"), which is the only thing that varies between
today's consumers. The Reviewer argued (PR debate) that approval is a *process* and should be
modelled as DATA — a workflow table an admin edits — not as generics that "grow type parameters
until it's a framework."

We chose generics, and drew a tripwire in the code (`ApprovalWorkflow`) at the buy-vs-build line:

> Generics stay until a **third, runtime-configured** consumer wants designer-editable steps. The
> moment a customer needs a process a non-programmer edits at runtime, that is a workflow ENGINE —
> and the right move is to **buy** one, not to grow a second type parameter onto this class until
> it becomes a bad framework.

A workflow table today would be an engine we have to build, test, and secure to avoid writing two
classes. That is the most expensive possible way to remove a duplication that two structs and a
noun already removed. The Reviewer signed off "under protest, tripwire noted" — the disagreement is
real and recorded, and the tripwire is where we agreed to revisit it.

## Decision 3: the YAGNI ledger — what we deliberately did NOT abstract

Extraction is a promise to generalise only the proven-shared shape. These were wanted by ZERO or
ONE consumer, so they stay out until a second use forces them (the ledger lives in code, on
`ApprovalWorkflow`, where the next person will read it):

- **Escalation / reminders**, **delegation / out-of-office**, **parallel approvers / quorum** —
  zero consumers.
- **N-step chains** — S09 invoices want a two-step approve. That is a second *shape*, not a
  parameter of this one; it earns its own type and only merges back if a third consumer shares it.
- **Per-subject approver roles** — `ApproverRoles` is T-invariant today (both flows use
  OrgAdmin/HrManager). It becomes a function of `T` the day a consumer needs a different set.
- **Reimbursement / post-approval actions** — expense-specific and kept on `ExpenseClaim`. The
  shared workflow ends at Approved/Denied/Cancelled; what a consumer does next is its own business.

## Decision 4: each consumer keeps its OWN status enum (minimal blast radius)

`LeaveRequestStatus` and `ExpenseClaimStatus` survive the extraction; only the guard *logic* moved
into the shared `ApprovalState`. The enums are pinned one-for-one with `ApprovalState`
(`ApprovalWorkflowTests`), so the boundary cast is total and safe.

Why not unify them into one enum? Because merged S06 tests, contracts, and DTO mappings all speak
`LeaveRequestStatus`; unifying would ripple through files the refactor has no business touching —
the opposite of "small, local, provably safe." Keeping each consumer's vocabulary is what let the
leave rewire be a **single-file domain diff** with no EF, migration, contract, or test changes.
`ExpenseClaim` adds the one exception it needs (a computed `Status` that reports `Reimbursed`),
declared unmapped in `ModelMappingTests`.

## Consequences

- A third approvable type costs a five-line marker struct + a noun + its own aggregate — the
  exercise ("add approval for employment-term changes") measures exactly this.
- The **cross-type approval inbox** (`GET /api/v1/approvals`) exists *because* the lifecycle is
  shared — one worklist over leave and expenses. It is the payoff made concrete.
- Cross-type cursor pagination is deferred: two id-spaces have no single cursor, so the inbox is a
  bounded, newest-first worklist (limit 1..100), not a paginated archive. Recorded, not hidden.
- Receipts are METADATA only (a url + filename); binary upload/storage is out of course scope.
- Reimbursement exists in the domain (approved → reimbursed) but has no endpoint this sprint: it
  moves money, so it belongs with the payment provider (S09) and the idempotency middleware (S10).

## Rejected alternatives

- **A runtime workflow-table engine.** An enormous dependency (or build) to avoid writing two
  small classes; hides the very logic the course teaches. Revisit only past the Decision-2 tripwire.
- **Abstract on the first consumer (S06).** Rejected there and vindicated here: the second consumer
  changed the shape (a sixth state, a decision reason) in ways a one-example guess would have
  mispredicted. Today's duplication became tomorrow's specification.
- **Unify the status enums.** Rejected — see Decision 4; it trades a small internal cast for a
  large cross-cutting diff.
- **A base class both aggregates inherit.** Inheritance couples identity and persistence (EF TPH)
  to share behaviour; composition via a guard the aggregates *call* shares the behaviour without
  the coupling.
