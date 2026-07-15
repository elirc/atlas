using Atlas.Domain.Approvals;

namespace Atlas.Domain.Leave;

/// <summary>Where a leave request sits in its lifecycle.</summary>
public enum LeaveRequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Denied = 4,
    Cancelled = 5,
}

/// <summary>
/// A worker's request to be away between two calendar days (inclusive). The lifecycle —
/// draft → submitted → approved/denied, cancellable from submitted or approved — is
/// guarded by METHODS, not open setters: an illegal transition throws, it does not
/// silently no-op. Overlap and balance checks are NOT here (they need other requests and
/// a policy) — they live in the approve use case.
/// </summary>
// 🔗 S08 UPDATE: the approval flow that was DELIBERATELY concrete in S06 is now rewired
// through the generic ApprovalWorkflow&lt;LeaveApprovalSubject&gt; — the extraction the S06
// review predicted, done on the SECOND consumer (expenses), not on speculation. The public
// surface here is byte-for-byte what it was: same LeaveRequestStatus, same properties, same
// guard messages. Only the guard LOGIC moved. The proof it preserved behaviour is that the
// S06 tests AND the S08 characterization tests are green and untouched — see ADR-0009.
public sealed class LeaveRequest
{
    public const int MaxNoteLength = 500;

    public Guid Id { get; }
    public Guid WorkerId { get; }
    public Guid OrgId { get; }

    public DateOnly From { get; }
    public DateOnly To { get; }

    /// <summary>Naive working-day count: weekdays in [From, To]. No holiday calendars.</summary>
    // WHY: weekdays only, no public-holiday calendars — those are explicitly out of scope
    // this sprint (they are a country-and-region reference-data problem of their own). The
    // count is computed once, at creation, and frozen: it is what was requested.
    public decimal WorkingDays { get; }

    public string? Note { get; }

    public LeaveRequestStatus Status { get; private set; }

    public Guid RequestedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    private LeaveRequest(
        Guid id,
        Guid workerId,
        Guid orgId,
        DateOnly from,
        DateOnly to,
        decimal workingDays,
        string? note,
        LeaveRequestStatus status,
        Guid requestedByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        WorkerId = workerId;
        OrgId = orgId;
        From = from;
        To = to;
        WorkingDays = workingDays;
        Note = note;
        Status = status;
        RequestedByUserId = requestedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public static LeaveRequest Draft(
        Guid workerId,
        Guid orgId,
        DateOnly from,
        DateOnly to,
        string? note,
        Guid requestedByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (workerId == Guid.Empty || orgId == Guid.Empty)
        {
            throw new DomainRuleException("A leave request needs a worker and an org.");
        }

        if (to < from)
        {
            throw new DomainRuleException("A leave request cannot end before it begins.");
        }

        string? trimmedNote = note?.Trim();
        if (trimmedNote is { Length: > MaxNoteLength })
        {
            throw new DomainRuleException($"Note must be at most {MaxNoteLength} characters.");
        }

        if (trimmedNote is { Length: 0 })
        {
            trimmedNote = null;
        }

        decimal workingDays = CountWorkingDays(from, to);
        if (workingDays <= 0m)
        {
            throw new DomainRuleException(
                "A leave request must cover at least one working day (weekends don't count).");
        }

        return new LeaveRequest(
            Guid.NewGuid(), workerId, orgId, from, to, workingDays, trimmedNote,
            LeaveRequestStatus.Draft, requestedByUserId, createdAtUtc);
    }

    // 🔍 THE REFACTOR, VISIBLE: each transition now asks the shared guard whether the move is
    // legal (throwing the SAME message it used to throw inline), then performs the state write.
    // The Draft→Submitted / Submitted→Approved etc. rules — and their exact wording — moved out
    // of a private RequireStatus on this class and into ApprovalWorkflow<LeaveApprovalSubject>.
    // The (ApprovalState) cast is total and safe: LeaveRequestStatus and ApprovalState share the
    // same members and values (ApprovalWorkflowTests pins it).
    public void Submit(DateTimeOffset atUtc)
    {
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanSubmit((ApprovalState)Status);
        Status = LeaveRequestStatus.Submitted;
        SubmittedAtUtc = atUtc;
    }

    public void Approve(Guid decidedByUserId, DateTimeOffset atUtc)
    {
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanApprove((ApprovalState)Status);
        Status = LeaveRequestStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = atUtc;
    }

    public void Deny(Guid decidedByUserId, DateTimeOffset atUtc)
    {
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanDeny((ApprovalState)Status);
        Status = LeaveRequestStatus.Denied;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = atUtc;
    }

    public void Cancel()
    {
        // WHY: a request can be pulled while pending OR after approval (plans change); a
        // denied or already-cancelled request is terminal. No timestamp is stamped here —
        // the audit entry the use case writes carries who cancelled and when.
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanCancel((ApprovalState)Status);
        Status = LeaveRequestStatus.Cancelled;
    }

    /// <summary>Do this request and the [otherFrom, otherTo] range share any calendar day?</summary>
    public bool Overlaps(DateOnly otherFrom, DateOnly otherTo) =>
        From <= otherTo && otherFrom <= To;

    private static decimal CountWorkingDays(DateOnly from, DateOnly to)
    {
        int count = 0;
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                count++;
            }
        }

        return count;
    }

    // WHY: EF Core materialization only.
    private LeaveRequest()
    {
    }
}
