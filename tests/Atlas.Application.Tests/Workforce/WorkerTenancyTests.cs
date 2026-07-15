using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Application.Workforce;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Workforce;

/// <summary>
/// Audit finding AF-3 (ledger #5): <see cref="GetWorker"/> read a worker by primary key with no
/// org filter and only <c>RequireUser()</c>, so any authenticated caller could read another
/// tenant's worker by id (IDOR). These tests pin the fix at the USE-CASE boundary — the HTTP
/// endpoint hid the leak behind a paired, org-scoped terms lookup, so the API matrix never saw it.
/// </summary>
// 🔗 Planted S03 (commit d5e4db3); the S02/S03 review praised the LIST endpoint's scoping and said
// nothing about the by-id path (docs/concepts/multi-tenancy.md: "which query could run without a
// context?"). This is what LGTM misses — see docs/security/sprint-13-audit.md#af-3.
public class WorkerTenancyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly Worker _mariaInA;

    public WorkerTenancyTests()
    {
        _mariaInA = Worker.Hire(
            OrgA, "Maria Santos", EmailAddress.From("maria@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 3, 1), Now);
        _workers.Rows.Add(_mariaInA);
    }

    private GetWorker For(ICurrentContext context) => new(_workers, context);

    [Fact]
    public async Task A_caller_in_another_org_cannot_read_a_workers_detail_by_id()
    {
        // The exposing case. Org B's admin knows (or guesses) org A's worker id.
        GetWorker useCase = For(StubContext.OrgUser(OrgB, Role.OrgAdmin));

        // WHY 404 and not 403: confirming the worker EXISTS is itself a cross-tenant leak
        // (ADR-0003). A foreign id is indistinguishable from one that never existed.
        await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(_mariaInA.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(Role.OrgAdmin)]
    [InlineData(Role.HrManager)]
    public async Task Admins_and_hr_managers_read_their_own_workers(Role role)
    {
        GetWorker useCase = For(StubContext.OrgUser(OrgA, role));

        Worker worker = await useCase.ExecuteAsync(_mariaInA.Id, CancellationToken.None);

        Assert.Equal("Maria Santos", worker.FullName);
    }

    [Fact]
    public async Task An_employee_cannot_open_the_worker_detail_view()
    {
        // Worker detail carries pay history — HR-manager-and-up, same bar as the audit log
        // and GetEngagement. An in-org role failure is a 403 (you may know it exists).
        GetWorker useCase = For(StubContext.OrgUser(OrgA, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => useCase.ExecuteAsync(_mariaInA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthenticated()
    {
        GetWorker useCase = For(StubContext.Anonymous());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => useCase.ExecuteAsync(_mariaInA.Id, CancellationToken.None));
    }
}
