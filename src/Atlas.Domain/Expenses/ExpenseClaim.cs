using Atlas.Domain.Approvals;

namespace Atlas.Domain.Expenses;

/// <summary>Where an expense claim sits — the shared approval states plus an expense-only tail.</summary>
// WHY a SIXTH state (Reimbursed) leave never had: an approved expense still owes the worker money,
// so "approved" and "paid back" are different facts. Reimbursement is deliberately NOT part of the
// shared ApprovalWorkflow (see its YAGNI ledger) — it is an expense-specific step layered on top.
// Values 1..5 line up with ApprovalState so the cast at the boundary is total; 6 is ours alone.
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
/// approved — is guarded by the shared <see cref="ApprovalWorkflow{T}"/>; reimbursement
/// (approved → reimbursed) is expense-specific and guarded here.
/// </summary>
// 🔗 This aggregate is the SECOND consumer of approval. It was born (S08 commit 1) as a
// deliberate copy-paste of S06's LeaveRequest shape — inline guards and all — precisely so that
// the extraction (commit 4) had two real examples to factor, not one example and a guess. Its
// approval transitions now delegate to ApprovalWorkflow<ExpenseApprovalSubject>; the SAME
// characterization tests that pinned the copy-pasted version still pass. See ADR-0009.
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

    // WHY metadata only: receipts as uploaded FILES (blob storage, virus scanning, signed URLs)
    // are a whole subsystem and explicitly out of scope this sprint (ADR-0009). A claim records
    // WHERE a receipt lives and what it is called; putting the bytes somewhere is a later problem.
    public string? ReceiptUrl { get; }
    public string? ReceiptFilename { get; }

    public string? Note { get; }

    /// <summary>The SHARED approval sub-state (Draft…Cancelled). Reimbursed is tracked separately.</summary>
    public ApprovalState Approval { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    /// <summary>When the claim was paid back. Null until reimbursed; set once, never cleared.</summary>
    public DateTimeOffset? ReimbursedAtUtc { get; private set; }

    public Guid RequestedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// The consumer-facing status: the shared <see cref="Approval"/> state, except a reimbursed
    /// claim reports <see cref="ExpenseClaimStatus.Reimbursed"/>. Computed, so it is declared
    /// unmapped in ModelMappingTests — the stored truth is <see cref="Approval"/> + <see cref="ReimbursedAtUtc"/>.
    /// </summary>
    public ExpenseClaimStatus Status =>
        ReimbursedAtUtc is not null ? ExpenseClaimStatus.Reimbursed : (ExpenseClaimStatus)Approval;

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
        Approval = ApprovalState.Draft;
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

    // The approval transitions delegate to the SHARED guard (same wording leave uses, with
    // "expense claim" as the noun). Reimburse does not — it is ours.
    public void Submit(DateTimeOffset atUtc)
    {
        ApprovalWorkflow<ExpenseApprovalSubject>.EnsureCanSubmit(Approval);
        Approval = ApprovalState.Submitted;
        SubmittedAtUtc = atUtc;
    }

    public void Approve(Guid decidedByUserId, DateTimeOffset atUtc, string? reason = null)
    {
        ApprovalWorkflow<ExpenseApprovalSubject>.EnsureCanApprove(Approval);
        Approval = ApprovalState.Approved;
        RecordDecision(decidedByUserId, atUtc, reason);
    }

    public void Deny(Guid decidedByUserId, DateTimeOffset atUtc, string? reason = null)
    {
        ApprovalWorkflow<ExpenseApprovalSubject>.EnsureCanDeny(Approval);
        Approval = ApprovalState.Denied;
        RecordDecision(decidedByUserId, atUtc, reason);
    }

    public void Cancel()
    {
        // GOTCHA: a reimbursed claim's Approval is still `Approved`, which the shared guard would
        // happily cancel. Reimbursement is terminal for OUR purposes, so we refuse here BEFORE
        // asking the shared guard — the extra terminal rule that is ours, not the workflow's.
        if (ReimbursedAtUtc is not null)
        {
            throw new DomainRuleException(
                $"A {Describe(Status)} expense claim cannot be cancelled.");
        }

        ApprovalWorkflow<ExpenseApprovalSubject>.EnsureCanCancel(Approval);
        Approval = ApprovalState.Cancelled;
    }

    /// <summary>Approved → Reimbursed. The expense-only tail the shared workflow does not model.</summary>
    // WHY the USE CASE for this ships later: reimbursement MOVES MONEY, so it belongs with the
    // payment provider (S09) and the idempotency middleware (S10). The domain state exists now so
    // the shape is complete and tested; no endpoint reaches it this sprint (ADR-0009 scope note).
    public void Reimburse(DateTimeOffset atUtc)
    {
        if (Status != ExpenseClaimStatus.Approved)
        {
            throw new DomainRuleException(
                $"Only an approved expense claim can be reimbursed (this one is {Describe(Status)}).");
        }

        ReimbursedAtUtc = atUtc;
    }

    /// <summary>The decision as a shared record, or null while still pending. A method, not a
    /// property, so ModelMappingTests does not try to map it.</summary>
    public ApprovalDecision? Decision() =>
        DecidedByUserId is { } decider
            ? new ApprovalDecision(decider, DecidedAtUtc!.Value, DecisionReason)
            : null;

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

    private static string Describe(ExpenseClaimStatus status) => status.ToString().ToLowerInvariant();

    // WHY: EF Core materialization only. Owned Amount is populated by EF; Approval is a value
    // type set through its private setter.
    private ExpenseClaim()
    {
        Amount = null!;
    }
}
