namespace Atlas.Domain.Expenses;

/// <summary>Where an expense claim sits — the shared approval states plus an expense-only tail.</summary>
// WHY a SIXTH state (Reimbursed) leave never had: an approved expense still owes the worker money,
// so "approved" and "paid back" are different facts. Reimbursement is expense-specific.
public enum ExpenseClaimStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Denied = 4,
    Cancelled = 5,
    Reimbursed = 6,
}

/// <summary>
/// A worker's claim to be paid back for money they spent: an amount (<see cref="Money"/>), a
/// category, and receipt METADATA (a url + filename — Atlas stores no binary this sprint). The
/// approval lifecycle — draft → submitted → approved/denied, cancellable from submitted or
/// approved — plus an expense-only reimbursed tail.
/// </summary>
// 🔗 The SECOND consumer of "approval". S06's LeaveRequest was the first, and it refused to
// generalise from a sample size of one. This aggregate is deliberately born as a COPY-PASTE of
// LeaveRequest's guard shape — RequireStatus/Describe inlined below, verbatim from leave with the
// noun changed — precisely so the extraction (a later commit) has TWO real examples to factor the
// right seams from, instead of one example and a guess. The differences (an amount, a receipt, a
// sixth Reimbursed state, a denial reason) are the point: a one-example abstraction would not have
// predicted them. See docs/concepts/rule-of-three.md.
public sealed class ExpenseClaim
{
    public const int MaxNoteLength = 500;
    public const int MaxReceiptUrlLength = 2048;
    public const int MaxReceiptFilenameLength = 260;

    public Guid Id { get; }
    public Guid WorkerId { get; }
    public Guid OrgId { get; }

    /// <summary>What the worker is owed. A Money value object — currency travels with the amount.</summary>
    public Money Amount { get; }

    public ExpenseCategory Category { get; }

    // WHY metadata only: receipts as uploaded FILES (blob storage, virus scanning, signed URLs) are
    // a whole subsystem, explicitly out of scope this sprint (ADR-0009).
    public string? ReceiptUrl { get; }
    public string? ReceiptFilename { get; }

    public string? Note { get; }

    /// <summary>The claim's status. Stored directly for now — the extraction will move the
    /// Draft…Cancelled portion into a shared ApprovalState.</summary>
    public ExpenseClaimStatus Status { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    /// <summary>When the claim was paid back. Null until reimbursed; set once, never cleared.</summary>
    public DateTimeOffset? ReimbursedAtUtc { get; private set; }

    public Guid RequestedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private ExpenseClaim(
        Guid id,
        Guid workerId,
        Guid orgId,
        Money amount,
        ExpenseCategory category,
        string? receiptUrl,
        string? receiptFilename,
        string? note,
        Guid requestedByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        WorkerId = workerId;
        OrgId = orgId;
        Amount = amount;
        Category = category;
        ReceiptUrl = receiptUrl;
        ReceiptFilename = receiptFilename;
        Note = note;
        Status = ExpenseClaimStatus.Draft;
        RequestedByUserId = requestedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public static ExpenseClaim Draft(
        Guid workerId,
        Guid orgId,
        Money amount,
        ExpenseCategory category,
        string? receiptUrl,
        string? receiptFilename,
        string? note,
        Guid requestedByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (workerId == Guid.Empty || orgId == Guid.Empty)
        {
            throw new DomainRuleException("An expense claim needs a worker and an org.");
        }

        if (amount is null || amount.Amount <= 0m)
        {
            throw new DomainRuleException("An expense claim must be for a positive amount.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainRuleException("An expense claim needs a known category.");
        }

        string? url = Clean(receiptUrl, MaxReceiptUrlLength, "Receipt URL");
        string? filename = Clean(receiptFilename, MaxReceiptFilenameLength, "Receipt filename");
        string? trimmedNote = Clean(note, MaxNoteLength, "Note");

        return new ExpenseClaim(
            Guid.NewGuid(), workerId, orgId, amount, category, url, filename, trimmedNote,
            requestedByUserId, createdAtUtc);
    }

    public void Submit(DateTimeOffset atUtc)
    {
        RequireStatus(ExpenseClaimStatus.Draft, "submitted");
        Status = ExpenseClaimStatus.Submitted;
        SubmittedAtUtc = atUtc;
    }

    public void Approve(Guid decidedByUserId, DateTimeOffset atUtc, string? reason = null)
    {
        RequireStatus(ExpenseClaimStatus.Submitted, "approved");
        Status = ExpenseClaimStatus.Approved;
        RecordDecision(decidedByUserId, atUtc, reason);
    }

    public void Deny(Guid decidedByUserId, DateTimeOffset atUtc, string? reason = null)
    {
        RequireStatus(ExpenseClaimStatus.Submitted, "denied");
        Status = ExpenseClaimStatus.Denied;
        RecordDecision(decidedByUserId, atUtc, reason);
    }

    public void Cancel()
    {
        if (Status is ExpenseClaimStatus.Reimbursed
            or not (ExpenseClaimStatus.Submitted or ExpenseClaimStatus.Approved))
        {
            throw new DomainRuleException(
                $"A {Describe(Status)} expense claim cannot be cancelled.");
        }

        Status = ExpenseClaimStatus.Cancelled;
    }

    public void Reimburse(DateTimeOffset atUtc)
    {
        if (Status != ExpenseClaimStatus.Approved)
        {
            throw new DomainRuleException(
                $"Only an approved expense claim can be reimbursed (this one is {Describe(Status)}).");
        }

        Status = ExpenseClaimStatus.Reimbursed;
        ReimbursedAtUtc = atUtc;
    }

    // ↓↓↓ COPIED VERBATIM FROM LeaveRequest, s/leave request/expense claim/ — the duplication the
    //     extraction will delete once ApprovalWorkflow<T> exists to own it. Two examples of THIS
    //     block, side by side, are what let the extraction factor the right seams.
    private void RequireStatus(ExpenseClaimStatus expected, string verb)
    {
        if (Status != expected)
        {
            throw new DomainRuleException(
                $"Only a {Describe(expected)} expense claim can be {verb} " +
                $"(this one is {Describe(Status)}).");
        }
    }

    private static string Describe(ExpenseClaimStatus status) => status.ToString().ToLowerInvariant();

    private void RecordDecision(Guid decidedByUserId, DateTimeOffset atUtc, string? reason)
    {
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = atUtc;
        DecisionReason = Clean(reason, MaxNoteLength, "Reason");
    }

    private static string? Clean(string? value, int maxLength, string label)
    {
        string? trimmed = value?.Trim();
        if (trimmed is { Length: 0 })
        {
            return null;
        }

        if (trimmed is not null && trimmed.Length > maxLength)
        {
            throw new DomainRuleException($"{label} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    // WHY: EF Core materialization only.
    private ExpenseClaim()
    {
        Amount = null!;
    }
}
