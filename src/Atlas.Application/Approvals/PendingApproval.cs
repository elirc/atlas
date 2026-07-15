namespace Atlas.Application.Approvals;

/// <summary>
/// One awaiting-decision item in the unified approval inbox, flattened from whichever aggregate it
/// came from. An application read model (like LeaveBalanceView) — the endpoint maps it to a DTO.
/// </summary>
// 🔗 THE PAYOFF, MADE CONCRETE. Because leave and expenses share the approval lifecycle, "what is
// waiting for me to decide?" is ONE question with ONE answer shape, across both types. Kind tells
// the caller which resource to POST the decision to; Summary is a human line for a worklist UI.
public sealed record PendingApproval(
    string Kind,
    Guid Id,
    Guid WorkerId,
    DateTimeOffset SubmittedAtUtc,
    string Summary);
