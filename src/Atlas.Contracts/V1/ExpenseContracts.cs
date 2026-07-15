namespace Atlas.Contracts.V1;

/// <summary>
/// Body of POST /api/v1/expenses — submit an expense claim for a worker (HR on behalf, v1).
/// The amount is a <see cref="MoneyDto"/> (S07): a decimal never travels without its currency.
/// </summary>
public sealed record SubmitExpenseRequest(
    Guid WorkerId,
    MoneyDto Amount,
    string? Category,
    string? ReceiptUrl,
    string? ReceiptFilename,
    string? Note);

/// <summary>Body of POST /api/v1/expenses/{id}/deny — an optional reason for the denial.</summary>
// WHY a body on deny but not on approve/cancel: a denial is the decision most likely to need a
// "why" the worker can read. It is optional — the endpoint accepts an empty/absent body.
public sealed record DenyExpenseRequest(string? Reason);

/// <summary>An expense claim as served by /api/v1/expenses.</summary>
public sealed record ExpenseClaimDto(
    Guid Id,
    Guid WorkerId,
    MoneyDto Amount,
    string Category,
    string Status,
    string? ReceiptUrl,
    string? ReceiptFilename,
    string? Note,
    Guid RequestedByUserId,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    DateTimeOffset? ReimbursedAtUtc,
    DateTimeOffset CreatedAtUtc);
