using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryPayCycleRepository : IPayCycleRepository
{
    public List<PayCycle> Rows { get; } = [];

    public Task AddAsync(PayCycle cycle, CancellationToken ct)
    {
        Rows.Add(cycle);
        return Task.CompletedTask;
    }

    public Task<PayCycle?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(c => c.Id == id && c.OrgId == orgId));

    public Task<PayCycle?> FindForCountryAsync(
        Guid orgId, CountryCode country, PayFrequency frequency, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(
            c => c.OrgId == orgId && c.Country == country && c.Frequency == frequency));

    public Task<IReadOnlyList<PayCycle>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PayCycle>>(
            [.. Rows.Where(c => c.OrgId == orgId).OrderByDescending(c => c.CreatedAtUtc)]);
}

public sealed class InMemoryPayrollRunRepository : IPayrollRunRepository
{
    public List<PayrollRun> Rows { get; } = [];

    public Task AddAsync(PayrollRun run, CancellationToken ct)
    {
        Rows.Add(run);
        return Task.CompletedTask;
    }

    public Task<PayrollRun?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == id && r.OrgId == orgId));

    public Task<PayrollRun?> FindByNaturalKeyAsync(
        Guid payCycleId, DateOnly periodStart, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(
            r => r.PayCycleId == payCycleId && r.PeriodStart == periodStart));

    public Task<IReadOnlyList<PayrollRun>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<PayrollRun> ordered = Rows
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(r =>
                r.CreatedAtUtc < cursor.CreatedAtUtc
                || (r.CreatedAtUtc == cursor.CreatedAtUtc
                    && r.Id.CompareTo(cursor.Id) < 0));
        }

        return Task.FromResult<IReadOnlyList<PayrollRun>>([.. ordered.Take(fetch)]);
    }
}

/// <summary>Reads payslips out of the runs the run-repository holds — mirrors the EF Include.</summary>
public sealed class InMemoryPayslipRepository(InMemoryPayrollRunRepository runs) : IPayslipRepository
{
    public Task<Payslip?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(runs.Rows
            .SelectMany(r => r.Payslips)
            .FirstOrDefault(p => p.Id == id && p.OrgId == orgId));
}

// NOTE: RecordingEventSink lives in EngagementFakes.cs (S05) and is reused here — it
// records AtlasEvents, the one IEventSink shape the whole codebase publishes.

/// <summary>An unpaid-leave calendar that reports a fixed number of days for everyone.</summary>
public sealed class StubUnpaidLeaveCalendar(int days = 0) : IUnpaidLeaveCalendar
{
    public int Days { get; set; } = days;

    public Task<int> ApprovedUnpaidDaysAsync(
        Guid workerId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) =>
        Task.FromResult(Days);
}
