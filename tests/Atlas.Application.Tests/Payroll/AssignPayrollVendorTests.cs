using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Tests.Payroll;

public class AssignPayrollVendorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryVendorAssignmentRepository _assignments = new();
    private readonly InMemoryCountryRepository _countries =
        new(new Country(CountryCode.From("DEU"), "Germany", "EUR"));
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private AssignPayrollVendor UseCase(StubContext? ctx = null) => new(
        _assignments, _countries, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId),
        new FixedClock(Now));

    [Fact]
    public async Task Appends_an_assignment_audits_and_announces()
    {
        VendorAssignment assignment = await UseCase().ExecuteAsync(
            "DEU", PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1), CancellationToken.None);

        Assert.Equal(PayrollVendor.LedgerPay, assignment.Vendor);
        Assert.Single(_assignments.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.vendor_assigned");
        Assert.Contains(_events.Published, e => e.Type == "payroll.vendor_assigned");
    }

    [Fact]
    public async Task A_duplicate_effective_day_is_refused()
    {
        await UseCase().ExecuteAsync("DEU", PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1), CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("DEU", PayrollVendor.AtlasNative, new DateOnly(2026, 8, 1), CancellationToken.None));

        // The failed second append wrote nothing.
        Assert.Single(_assignments.Rows);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task An_unsupported_country_is_a_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase().ExecuteAsync("PHL", PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Only_org_admins_may_assign_a_vendor()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.HrManager, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            useCase.ExecuteAsync("DEU", PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1), CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }
}
