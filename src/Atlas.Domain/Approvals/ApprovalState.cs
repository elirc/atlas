namespace Atlas.Domain.Approvals;

/// <summary>
/// The lifecycle a thing-awaiting-a-decision walks: someone drafts it, submits it, and an
/// approver either approves or denies it; a pending or approved item can still be cancelled.
/// This is the vocabulary S06's leave flow and S08's expense flow BOTH spoke — extracted
/// here (S08) once the second consumer proved the shape was shared, not coincidental.
/// </summary>
// LEARN: the integer values are pinned (Draft = 1 … Cancelled = 5) and match
// LeaveRequestStatus and ExpenseClaimStatus one-for-one ON PURPOSE. Each consumer keeps its
// OWN status enum (its own vocabulary at its own boundary); this is the shared lingua franca
// the guards speak. The alignment is not a coincidence to trust — ApprovalWorkflowTests pins
// it, so the boundary cast `(ApprovalState)LeaveRequestStatus.X` can never silently rot.
public enum ApprovalState
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Denied = 4,
    Cancelled = 5,
}
