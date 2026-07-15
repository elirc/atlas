using Atlas.Domain;
using Atlas.Domain.Expenses;

namespace Atlas.Domain.Tests.Expenses;

/// <summary>
/// CHARACTERIZATION TESTS for the expense approval lifecycle — the OTHER half of the S08 safety net.
/// </summary>
// 📘 Written against the copy-pasted, INLINE approval flow (commit 1) — before ApprovalWorkflow
// existed. They pin the same shape leave has: draft → submitted → approved/denied, cancellable
// from submitted or approved, with the exact "expense claim" wording. When the extraction rewired
// ExpenseClaim's transitions through ApprovalWorkflow<ExpenseApprovalSubject> (commit 4), this file
// did not change and stayed green — proof the second consumer's behaviour was preserved too.
public class ExpenseApprovalCharacterizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();

    private static ExpenseClaim Draft() => ExpenseClaim.Draft(
        WorkerId, OrgId, Money.Of(240m, CurrencyCode.From("USD")), ExpenseCategory.Travel,
        "https://receipts.example/abc", "taxi.pdf", null, Hr, Now);

    [Fact]
    public void Submit_then_approve_records_the_decider()
    {
        ExpenseClaim claim = Draft();
        Assert.Equal(ExpenseClaimStatus.Draft, claim.Status);

        claim.Submit(Now);
        Assert.Equal(ExpenseClaimStatus.Submitted, claim.Status);
        Assert.Equal(Now, claim.SubmittedAtUtc);

        var decider = Guid.NewGuid();
        claim.Approve(decider, Now);
        Assert.Equal(ExpenseClaimStatus.Approved, claim.Status);
        Assert.Equal(decider, claim.DecidedByUserId);
        Assert.Equal(Now, claim.DecidedAtUtc);
    }

    [Fact]
    public void Approving_before_submitting_throws_the_pinned_message()
    {
        var ex = Assert.Throws<DomainRuleException>(() => Draft().Approve(Guid.NewGuid(), Now));
        Assert.Equal(
            "Only a submitted expense claim can be approved (this one is draft).", ex.Message);
    }

    [Fact]
    public void Deny_can_record_a_reason()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Deny(Guid.NewGuid(), Now, "Missing itemised receipt.");

        Assert.Equal(ExpenseClaimStatus.Denied, claim.Status);
        Assert.Equal("Missing itemised receipt.", claim.DecisionReason);
    }

    [Fact]
    public void Denied_claims_are_terminal()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Deny(Guid.NewGuid(), Now);

        Assert.Equal(
            "A denied expense claim cannot be cancelled.",
            Assert.Throws<DomainRuleException>(() => claim.Cancel()).Message);
        Assert.Throws<DomainRuleException>(() => claim.Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Approved_claims_can_still_be_cancelled()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Approve(Guid.NewGuid(), Now);
        claim.Cancel();

        Assert.Equal(ExpenseClaimStatus.Cancelled, claim.Status);
    }

    [Fact]
    public void A_cancelled_claim_cannot_be_cancelled_again()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Cancel();

        Assert.Equal(
            "A cancelled expense claim cannot be cancelled.",
            Assert.Throws<DomainRuleException>(() => claim.Cancel()).Message);
    }
}
