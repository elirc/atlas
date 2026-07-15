using Atlas.Domain.Approvals;

namespace Atlas.Domain.Leave;

/// <summary>Marker that lets <see cref="ApprovalWorkflow{T}"/> say "leave request" in its messages.</summary>
// WHY a zero-field struct: it carries no data and is never instantiated — the workflow reads
// SubjectNoun off the TYPE. It exists only to close the generic (`ApprovalWorkflow<LeaveApprovalSubject>`)
// and pin the noun, so S06's exact guard messages survive the extraction unchanged.
public readonly struct LeaveApprovalSubject : IApprovalSubject
{
    public static string SubjectNoun => "leave request";
}
