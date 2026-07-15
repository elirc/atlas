using Atlas.Domain.Approvals;

namespace Atlas.Domain.Expenses;

/// <summary>Marker that lets <see cref="ApprovalWorkflow{T}"/> say "expense claim" in its messages.</summary>
// 🔗 The twin of LeaveApprovalSubject. Two markers, one workflow — that IS the payoff of the
// extraction: the second consumer needed only a five-line struct and a noun to inherit the whole
// guarded lifecycle, its exact messages, and its approver-role resolution.
public readonly struct ExpenseApprovalSubject : IApprovalSubject
{
    public static string SubjectNoun => "expense claim";
}
