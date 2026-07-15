namespace Atlas.Contracts.V1;

/// <summary>
/// One item in GET /api/v1/approvals — a thing awaiting the caller's decision, from ANY approvable
/// type. <see cref="Kind"/> ("leave" | "expense") tells the client which resource to POST the
/// approve/deny/cancel to; <see cref="Summary"/> is a one-line human description for a worklist.
/// </summary>
public sealed record ApprovalInboxItemDto(
    string Kind,
    Guid Id,
    Guid WorkerId,
    DateTimeOffset SubmittedAtUtc,
    string Summary);
