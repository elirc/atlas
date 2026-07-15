using Atlas.Domain;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Leave;

public class LeaveRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();

    // 2026-08-03 is a Monday; 2026-08-07 is the Friday of the same week.
    private static LeaveRequest Draft(string from = "2026-08-03", string to = "2026-08-07") =>
        LeaveRequest.Draft(WorkerId, OrgId, DateOnly.Parse(from), DateOnly.Parse(to), null, Hr, Now);

    [Fact]
    public void Draft_counts_weekdays_only()
    {
        Assert.Equal(5m, Draft().WorkingDays);                       // Mon..Fri
        Assert.Equal(6m, Draft("2026-08-03", "2026-08-10").WorkingDays); // Mon..next Mon spans a weekend
    }

    [Fact]
    public void Draft_rejects_a_weekend_only_range()
    {
        // Sat 2026-08-08 .. Sun 2026-08-09 — zero working days.
        Assert.Throws<DomainRuleException>(() => LeaveRequest.Draft(
            WorkerId, OrgId, new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 9), null, Hr, Now));
    }

    [Fact]
    public void Draft_rejects_end_before_start()
    {
        Assert.Throws<DomainRuleException>(() => LeaveRequest.Draft(
            WorkerId, OrgId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 3), null, Hr, Now));
    }

    [Fact]
    public void Draft_trims_a_blank_note_to_null()
    {
        var request = LeaveRequest.Draft(
            WorkerId, OrgId, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 7), "   ", Hr, Now);

        Assert.Null(request.Note);
    }

    [Fact]
    public void Lifecycle_submit_then_approve_records_the_decider()
    {
        var request = Draft();
        Assert.Equal(LeaveRequestStatus.Draft, request.Status);

        request.Submit(Now);
        Assert.Equal(LeaveRequestStatus.Submitted, request.Status);
        Assert.Equal(Now, request.SubmittedAtUtc);

        var decider = Guid.NewGuid();
        request.Approve(decider, Now);
        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
        Assert.Equal(decider, request.DecidedByUserId);
        Assert.Equal(Now, request.DecidedAtUtc);
    }

    [Fact]
    public void Cannot_approve_before_submitting()
    {
        Assert.Throws<DomainRuleException>(() => Draft().Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Cannot_approve_twice()
    {
        var request = Draft();
        request.Submit(Now);
        request.Approve(Guid.NewGuid(), Now);

        Assert.Throws<DomainRuleException>(() => request.Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Denied_requests_are_terminal()
    {
        var request = Draft();
        request.Submit(Now);
        request.Deny(Guid.NewGuid(), Now);

        Assert.Throws<DomainRuleException>(() => request.Cancel());
        Assert.Throws<DomainRuleException>(() => request.Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Approved_requests_can_still_be_cancelled()
    {
        var request = Draft();
        request.Submit(Now);
        request.Approve(Guid.NewGuid(), Now);
        request.Cancel();

        Assert.Equal(LeaveRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void A_cancelled_request_cannot_be_cancelled_again()
    {
        var request = Draft();
        request.Submit(Now);
        request.Cancel();

        Assert.Throws<DomainRuleException>(() => request.Cancel());
    }

    [Theory]
    [InlineData("2026-08-05", "2026-08-10", true)]  // starts inside the base range
    [InlineData("2026-08-07", "2026-08-09", true)]  // touches on the 7th (inclusive)
    [InlineData("2026-08-10", "2026-08-14", false)] // begins after it ends
    [InlineData("2026-07-27", "2026-08-02", false)] // ends the day before it begins
    public void Overlaps_detects_shared_days(string from, string to, bool expected)
    {
        // Base range: 2026-08-03 .. 2026-08-07.
        Assert.Equal(expected, Draft().Overlaps(DateOnly.Parse(from), DateOnly.Parse(to)));
    }
}
