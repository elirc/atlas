using Atlas.Domain.Access;

namespace Atlas.Domain.Approvals;

/// <summary>
/// The generic approval guard: given the current <see cref="ApprovalState"/>, it either lets a
/// transition proceed or throws a <see cref="DomainRuleException"/> whose message names the thing
/// being decided (via <typeparamref name="T"/>). It holds NO state — the aggregate owns that — and
/// mutates nothing; it answers "may this move happen, and if not, why not, in words a caller can
/// show a user?". S06's leave flow and S08's expense flow both call these guards.
/// </summary>
/// <typeparam name="T">
/// A marker (<see cref="IApprovalSubject"/>) supplying the noun for messages — the ONLY thing that
/// varies between consumers. See the tripwire below on why it stays the only type parameter.
/// </typeparam>
// ⚠️ THE YAGNI LEDGER — what this component deliberately does NOT do, and why. Read before adding.
// Extraction is a promise to abstract ONLY what a second consumer proved was shared. These are the
// things exactly ONE (or zero) consumers wanted, so they stay OUT until a third use forces them:
//
//   · Escalation / reminders  — "auto-approve after 7 days", "nag the approver". Zero consumers.
//   · Delegation / out-of-office — reassigning an approver. Zero consumers; would drag an
//     approver-assignment model in with it.
//   · N-step / multi-stage chains — S09 invoices want a TWO-step approve (manager then finance).
//     That is a second SHAPE, not a parameter of this one; when it lands it earns its own type,
//     and only if a THIRD consumer shares it does it merge back here.
//   · Parallel approvers / quorum — "any 2 of 3 managers". Zero consumers.
//   · Per-subject approver roles — today ApproverRoles is the same set for every T (below). The
//     day expenses need a different approver set than leave, THAT is when role resolution becomes
//     a function of T — not before.
//   · Reimbursement / post-approval actions — expense-specific and stays on ExpenseClaim. The
//     workflow ends at Approved/Denied/Cancelled; what a consumer does after is its own business.
//
// 🔧 TRIPWIRE — the buy-vs-build line. Everything above is "build a little more, in code, when a
// SECOND consumer shares it." But the moment a customer needs a DESIGNER-EDITABLE process — steps
// configured at runtime, in data, by a non-programmer — stop. That is not more generics; it is a
// workflow ENGINE, and the cheapest correct answer is to BUY one, not to grow a second type
// parameter onto this class until it becomes a bad one. If you find yourself adding TState or a
// steps-table here, you have crossed the line — reach for the debate thread (PR) and ADR-0009,
// not the keyboard.
public static class ApprovalWorkflow<T>
    where T : IApprovalSubject
{
    /// <summary>Who may approve or deny — org admins and HR managers (ops always passes upstream).</summary>
    // WHY here: "who decides" is part of the approval process, so the use cases source it from the
    // component instead of each re-listing the roles. Today it is T-invariant (same set for leave
    // and expenses) — deliberately (see the ledger). It is a Role[] so it drops straight into
    // ICurrentContext.RequireRole(params Role[]).
    public static Role[] ApproverRoles => [Role.OrgAdmin, Role.HrManager];

    /// <summary>Guard the Draft → Submitted move. Throws if the current state is not Draft.</summary>
    public static void EnsureCanSubmit(ApprovalState current) =>
        RequireCurrent(current, ApprovalState.Draft, "submitted");

    /// <summary>Guard the Submitted → Approved move. Throws if the current state is not Submitted.</summary>
    public static void EnsureCanApprove(ApprovalState current) =>
        RequireCurrent(current, ApprovalState.Submitted, "approved");

    /// <summary>Guard the Submitted → Denied move. Throws if the current state is not Submitted.</summary>
    public static void EnsureCanDeny(ApprovalState current) =>
        RequireCurrent(current, ApprovalState.Submitted, "denied");

    /// <summary>
    /// Guard cancellation. Legal from Submitted OR Approved (plans change); a draft, denied, or
    /// already-cancelled item is refused with a message that names the current state.
    /// </summary>
    public static void EnsureCanCancel(ApprovalState current)
    {
        // WHY a bespoke message, not the RequireCurrent template: cancellation has TWO legal
        // sources, so "only a submitted X can be cancelled" would be a lie. The wording here is
        // verbatim what LeaveRequest.Cancel said in S06 — preserving it is what keeps the
        // characterization tests green through the extraction.
        if (current is not (ApprovalState.Submitted or ApprovalState.Approved))
        {
            throw new DomainRuleException(
                $"A {Describe(current)} {T.SubjectNoun} cannot be cancelled.");
        }
    }

    private static void RequireCurrent(ApprovalState current, ApprovalState required, string verb)
    {
        if (current != required)
        {
            throw new DomainRuleException(
                $"Only a {Describe(required)} {T.SubjectNoun} can be {verb} " +
                $"(this one is {Describe(current)}).");
        }
    }

    private static string Describe(ApprovalState state) => state.ToString().ToLowerInvariant();
}
