using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

public class PayrollRunStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PayrollRun DraftRun() =>
        PayrollRun.Create(Guid.NewGuid(), Guid.NewGuid(), PayPeriod.ForMonth(2026, 3), Guid.NewGuid(), T0);

    // ── The exhaustive matrix: every (state × transition), legal AND illegal ────────────

    public static TheoryData<PayrollRunState, PayrollTransition> AllMoves()
    {
        var data = new TheoryData<PayrollRunState, PayrollTransition>();
        foreach (PayrollRunState from in Enum.GetValues<PayrollRunState>())
        {
            foreach (PayrollTransition move in Enum.GetValues<PayrollTransition>())
            {
                data.Add(from, move);
            }
        }

        return data;
    }

    // 🔍 The illegal half of this matrix is the valuable half. The table lists 4 legal
    // moves; this test exercises all 4 × 4 = 16 cells, proving the other 12 throw.
    [Theory]
    [MemberData(nameof(AllMoves))]
    public void Guard_agrees_with_the_legal_table(PayrollRunState from, PayrollTransition move)
    {
        bool isLegal = PayrollRunStateMachine.LegalMoves.Any(m => m.From == from && m.Move == move);

        if (isLegal)
        {
            (_, _, PayrollRunState expected) =
                PayrollRunStateMachine.LegalMoves.Single(m => m.From == from && m.Move == move);
            Assert.Equal(expected, PayrollRunStateMachine.Next(from, move));
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => PayrollRunStateMachine.Next(from, move));
        }
    }

    [Fact]
    public void The_table_lists_exactly_the_four_legal_moves()
    {
        Assert.Equal(4, PayrollRunStateMachine.LegalMoves.Count);
    }

    // ── The aggregate walks the states through its guarded methods ───────────────────────

    [Fact]
    public void Happy_path_walks_draft_to_executed()
    {
        PayrollRun run = DraftRun();
        Assert.Equal(PayrollRunState.Draft, run.State);

        run.SubmitForReview();
        Assert.Equal(PayrollRunState.InReview, run.State);

        run.Approve();
        Assert.Equal(PayrollRunState.Approved, run.State);

        run.Execute(T0.AddDays(1));
        Assert.Equal(PayrollRunState.Executed, run.State);
        Assert.Equal(T0.AddDays(1), run.ExecutedAtUtc);
    }

    [Fact]
    public void Reject_returns_a_reviewed_run_to_draft()
    {
        PayrollRun run = DraftRun();
        run.SubmitForReview();

        run.Reject();

        Assert.Equal(PayrollRunState.Draft, run.State);
    }

    [Fact]
    public void A_draft_run_cannot_be_executed()
    {
        PayrollRun run = DraftRun();

        // Authorization follows STATE: not even a legitimate caller can execute a Draft.
        Assert.Throws<DomainRuleException>(() => run.Execute(T0));
        Assert.Null(run.ExecutedAtUtc);
    }

    [Fact]
    public void An_executed_run_is_terminal()
    {
        PayrollRun run = DraftRun();
        run.SubmitForReview();
        run.Approve();
        run.Execute(T0);

        Assert.Throws<DomainRuleException>(() => run.SubmitForReview());
        Assert.Throws<DomainRuleException>(() => run.Approve());
        Assert.Throws<DomainRuleException>(() => run.Execute(T0));
    }

    [Fact]
    public void Payslips_cannot_be_added_once_a_run_leaves_draft()
    {
        PayrollRun run = DraftRun();
        run.SubmitForReview();

        Payslip slip = Payslip.FromCalculation(
            run.Id, Guid.NewGuid(), run.OrgId, PayPeriod.ForMonth(2026, 3),
            Sample(), T0);

        Assert.Throws<DomainRuleException>(() => run.AddPayslip(slip));
    }

    private static PayslipCalculation Sample()
    {
        var pack = CountryRulePacks.For(CountryCode.From("DEU"));
        var record = Atlas.Domain.Workforce.EmploymentRecord.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), "Engineer", 60_000m, "EUR", 40, Guid.NewGuid(), T0);
        return PayrollCalculator.Calculate(
            new PayrollCalculationInput([record], pack, PayPeriod.ForMonth(2026, 3), 0));
    }
}
