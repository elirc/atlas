using System.Runtime.CompilerServices;
using Atlas.Application.Abstractions;
using Atlas.Application.Reporting;

namespace Atlas.Application.Tests.Fakes;

/// <summary>
/// A scripted <see cref="IReportQueries"/> for use-case tests: set the rows it should return, then
/// assert the use case's authz/validation/fold behaviour without a database. The projection SQL is
/// tested against real SQLite in Api.Tests; here we test the use cases ABOVE the port.
/// </summary>
public sealed class InMemoryReportQueries : IReportQueries
{
    public List<HeadcountRow> Headcount { get; } = [];
    public List<EmploymentCostRow> Cost { get; } = [];
    public List<UpcomingRenewalRow> Renewals { get; } = [];
    public List<WorkerComplianceInput> RosterInputs { get; } = [];

    public (int Year, int Month)? LastCostRequest { get; private set; }

    public Task<IReadOnlyList<HeadcountRow>> HeadcountByCountryAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HeadcountRow>>(Headcount);

    public Task<IReadOnlyList<EmploymentCostRow>> EmploymentCostByCountryMonthAsync(
        Guid orgId, int year, int month, CancellationToken cancellationToken)
    {
        LastCostRequest = (year, month);
        return Task.FromResult<IReadOnlyList<EmploymentCostRow>>(Cost);
    }

    public Task<IReadOnlyList<UpcomingRenewalRow>> UpcomingRenewalsAsync(
        Guid orgId, DateOnly through, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UpcomingRenewalRow>>(Renewals);

    public async IAsyncEnumerable<UpcomingRenewalRow> StreamUpcomingRenewalsAsync(
        Guid orgId, DateOnly through, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (UpcomingRenewalRow row in Renewals)
        {
            yield return row;
        }

        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkerComplianceInput>> ActiveWorkersWithDocumentsAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkerComplianceInput>>(RosterInputs);
}
