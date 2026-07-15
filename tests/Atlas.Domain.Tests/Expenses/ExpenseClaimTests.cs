using Atlas.Domain;
using Atlas.Domain.Approvals;
using Atlas.Domain.Expenses;

namespace Atlas.Domain.Tests.Expenses;

/// <summary>
/// Expense-SPECIFIC domain rules: the bits that are NOT the shared approval lifecycle — amount and
/// receipt validation, and the reimbursement tail the shared workflow deliberately does not model.
/// </summary>
public class ExpenseClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();

    private static Money Usd(decimal amount) => Money.Of(amount, CurrencyCode.From("USD"));

    private static ExpenseClaim Draft(
        decimal amount = 240m,
        ExpenseCategory category = ExpenseCategory.Travel,
        string? receiptUrl = "https://receipts.example/abc",
        string? receiptFilename = "taxi.pdf",
        string? note = null) =>
        ExpenseClaim.Draft(WorkerId, OrgId, Usd(amount), category, receiptUrl, receiptFilename, note, Hr, Now);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Draft_rejects_a_non_positive_amount(decimal amount) =>
        Assert.Throws<DomainRuleException>(() => Draft(amount));

    [Fact]
    public void Draft_rejects_an_unknown_category() =>
        Assert.Throws<DomainRuleException>(() => Draft(category: (ExpenseCategory)999));

    [Fact]
    public void Draft_rejects_an_overlong_receipt_url() =>
        Assert.Throws<DomainRuleException>(() =>
            Draft(receiptUrl: new string('u', ExpenseClaim.MaxReceiptUrlLength + 1)));

    [Fact]
    public void Draft_trims_a_blank_note_to_null()
    {
        ExpenseClaim claim = Draft(note: "   ");
        Assert.Null(claim.Note);
    }

    [Fact]
    public void Draft_allows_missing_receipt_metadata()
    {
        ExpenseClaim claim = Draft(receiptUrl: null, receiptFilename: null);
        Assert.Null(claim.ReceiptUrl);
        Assert.Null(claim.ReceiptFilename);
    }

    [Fact]
    public void A_new_claim_is_draft()
    {
        ExpenseClaim claim = Draft();
        Assert.Equal(ApprovalState.Draft, claim.Approval);
        Assert.Equal(ExpenseClaimStatus.Draft, claim.Status);
    }

    // ── Reimbursement: the expense-only tail (approved → reimbursed). ─────────────────────────────
    [Fact]
    public void Reimburse_requires_an_approved_claim()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);

        var ex = Assert.Throws<DomainRuleException>(() => claim.Reimburse(Now));
        Assert.Equal(
            "Only an approved expense claim can be reimbursed (this one is submitted).", ex.Message);
    }

    [Fact]
    public void Reimburse_moves_an_approved_claim_to_reimbursed()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Approve(Guid.NewGuid(), Now);

        claim.Reimburse(Now);

        Assert.Equal(ExpenseClaimStatus.Reimbursed, claim.Status);
        Assert.Equal(Now, claim.ReimbursedAtUtc);
        // The shared approval sub-state is unchanged — reimbursement is layered on top of Approved.
        Assert.Equal(ApprovalState.Approved, claim.Approval);
    }

    [Fact]
    public void A_reimbursed_claim_cannot_be_reimbursed_again()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Approve(Guid.NewGuid(), Now);
        claim.Reimburse(Now);

        Assert.Throws<DomainRuleException>(() => claim.Reimburse(Now));
    }

    [Fact]
    public void A_reimbursed_claim_cannot_be_cancelled()
    {
        ExpenseClaim claim = Draft();
        claim.Submit(Now);
        claim.Approve(Guid.NewGuid(), Now);
        claim.Reimburse(Now);

        var ex = Assert.Throws<DomainRuleException>(() => claim.Cancel());
        Assert.Equal("A reimbursed expense claim cannot be cancelled.", ex.Message);
    }
}
