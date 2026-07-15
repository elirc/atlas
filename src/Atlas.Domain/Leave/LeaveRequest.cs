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
// 🔗 LEARN: this approval flow is DELIBERATELY concrete — states, a decider, a decision
// timestamp, all inlined on this one entity. Expenses (S8) will be the SECOND consumer,
// and that is when the generic ApprovalWorkflow&lt;T&gt; gets extracted — on the second
// real use, not on this speculation. The shape here is the specification S8 factors out.
// Copying once is cheaper than the wrong abstraction; today's duplication is tomorrow's
// spec. (The S6 review debate names this out loud; the Reviewer predicts the extraction.)
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

    public void Submit(DateTimeOffset atUtc)
    {
        RequireStatus(LeaveRequestStatus.Draft, "submitted");
        Status = LeaveRequestStatus.Submitted;
        SubmittedAtUtc = atUtc;
    }

    public void Approve(Guid decidedByUserId, DateTimeOffset atUtc)
    {
        RequireStatus(LeaveRequestStatus.Submitted, "approved");
        Status = LeaveRequestStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = atUtc;
    }

    public void Deny(Guid decidedByUserId, DateTimeOffset atUtc)
    {
        RequireStatus(LeaveRequestStatus.Submitted, "denied");
        Status = LeaveRequestStatus.Denied;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = atUtc;
    }

    public void Cancel()
    {
        // WHY: a request can be pulled while pending OR after approval (plans change); a
        // denied or already-cancelled request is terminal. No timestamp is stamped here —
        // the audit entry the use case writes carries who cancelled and when.
        if (Status is not (LeaveRequestStatus.Submitted or LeaveRequestStatus.Approved))
        {
            throw new DomainRuleException(
                $"A {Describe(Status)} leave request cannot be cancelled.");
        }

        Status = LeaveRequestStatus.Cancelled;
    }

    /// <summary>Do this request and the [otherFrom, otherTo] range share any calendar day?</summary>
    public bool Overlaps(DateOnly otherFrom, DateOnly otherTo) =>
        From <= otherTo && otherFrom <= To;

    private void RequireStatus(LeaveRequestStatus expected, string verb)
    {
        if (Status != expected)
        {
            throw new DomainRuleException(
                $"Only a {Describe(expected)} leave request can be {verb} " +
                $"(this one is {Describe(Status)}).");
        }
    }

    private static string Describe(LeaveRequestStatus status) =>
        status.ToString().ToLowerInvariant();

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
