using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Payroll;

public class PayrollReadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly InMemoryPayrollRunRepository _runs = new();
    private readonly InMemoryPayslipRepository _payslips;

    public PayrollReadsTests() => _payslips = new InMemoryPayslipRepository(_runs);

    private (PayrollRun Run, Guid PayslipId) SeedRun(Guid orgId)
    {
        var pack = CountryRulePacks.For(CountryCode.From("DEU"));
        var period = PayPeriod.ForMonth(2026, 3);
        var record = EmploymentRecord.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), "Engineer", 60_000m, "EUR", 40, Guid.NewGuid(), Now);

        var run = PayrollRun.Create(orgId, Guid.NewGuid(), period, Guid.NewGuid(), Now);
        var calc = PayrollCalculator.Calculate(new PayrollCalculationInput([record], pack, period, 0));
        run.AddPayslip(Payslip.FromCalculation(run.Id, record.WorkerId, orgId, period, calc, Now));

        _runs.Rows.Add(run);
        return (run, run.Payslips[0].Id);
    }

    private GetPayrollRun GetRun(Guid orgId) => new(_runs, StubContext.OrgUser(orgId, Role.OrgAdmin));

    private GetPayslip GetSlip(Guid orgId) => new(_payslips, StubContext.OrgUser(orgId, Role.OrgAdmin));

    [Fact]
    public async Task A_run_is_readable_within_its_own_org()
    {
        (PayrollRun run, _) = SeedRun(OrgA);

        PayrollRun read = await GetRun(OrgA).ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(run.Id, read.Id);
        Assert.Single(read.Payslips);
    }

    [Fact]
    public async Task A_foreign_org_run_does_not_exist_for_the_caller()
    {
        (PayrollRun run, _) = SeedRun(OrgA);

        // WHY 404: existence across tenant boundaries is need-to-know (ADR-0003).
        await Assert.ThrowsAsync<NotFoundException>(() =>
            GetRun(OrgB).ExecuteAsync(run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_payslip_is_readable_within_its_own_org()
    {
        (_, Guid payslipId) = SeedRun(OrgA);

        Payslip read = await GetSlip(OrgA).ExecuteAsync(payslipId, CancellationToken.None);

        Assert.Equal(payslipId, read.Id);
    }

    [Fact]
    public async Task A_foreign_org_payslip_does_not_exist_for_the_caller()
    {
        (_, Guid payslipId) = SeedRun(OrgA);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            GetSlip(OrgB).ExecuteAsync(payslipId, CancellationToken.None));
    }
}
