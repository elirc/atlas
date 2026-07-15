using Atlas.Domain;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Leave;

/// <summary>
/// CHARACTERIZATION TESTS for leave's approval lifecycle — the safety net for the S08 extraction.
/// </summary>
// 📘 Characterization tests describe what code DOES, not what it should do. Written BEFORE moving
// anything (commit 2), they pin the CURRENT behaviour — including the exact guard-message text —
// so the refactor that reroutes these transitions through ApprovalWorkflow<LeaveApprovalSubject>
// can be PROVEN behaviour-preserving: this file does not change, and it stays green. If a message
// or a state had drifted during the extraction, one of these assertions would have gone red. That
// is the whole method: the test is the contract; the refactor honours it or is not a refactor.
public class LeaveApprovalCharacterizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();

    // 2026-08-03 is a Monday; 2026-08-07 is the Friday of the same week.
    private static LeaveRequest Draft() =>
        LeaveRequest.Draft(WorkerId, OrgId, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 7), null, Hr, Now);

    [Fact]
    public void Submit_then_approve_records_the_decider()
    {
        LeaveRequest request = Draft();
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
    public void Approving_before_submitting_throws_the_pinned_message()
    {
        var ex = Assert.Throws<DomainRuleException>(() => Draft().Approve(Guid.NewGuid(), Now));
        Assert.Equal(
            "Only a submitted leave request can be approved (this one is draft).", ex.Message);
    }

    [Fact]
    public void Approving_twice_throws()
    {
        LeaveRequest request = Draft();
        request.Submit(Now);
        request.Approve(Guid.NewGuid(), Now);

        var ex = Assert.Throws<DomainRuleException>(() => request.Approve(Guid.NewGuid(), Now));
        Assert.Equal(
            "Only a submitted leave request can be approved (this one is approved).", ex.Message);
    }

    [Fact]
    public void Denied_requests_are_terminal()
    {
        LeaveRequest request = Draft();
        request.Submit(Now);
        request.Deny(Guid.NewGuid(), Now);

        Assert.Equal(LeaveRequestStatus.Denied, request.Status);
        Assert.Equal(
            "A denied leave request cannot be cancelled.",
            Assert.Throws<DomainRuleException>(() => request.Cancel()).Message);
        Assert.Throws<DomainRuleException>(() => request.Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Approved_requests_can_still_be_cancelled()
    {
        LeaveRequest request = Draft();
        request.Submit(Now);
        request.Approve(Guid.NewGuid(), Now);
        request.Cancel();

        Assert.Equal(LeaveRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void A_cancelled_request_cannot_be_cancelled_again()
    {
        LeaveRequest request = Draft();
        request.Submit(Now);
        request.Cancel();

        Assert.Equal(
            "A cancelled leave request cannot be cancelled.",
            Assert.Throws<DomainRuleException>(() => request.Cancel()).Message);
    }
}
