using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class WorkerEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public WorkerEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid OrgId)> OrgAdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Worker Org {Guid.NewGuid():N}"[..20], $"wk-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return (client, session.User.OrgId!.Value);
    }

    private static HireWorkerRequest Maria(string hired = "2026-08-01") => new(
        "Maria Santos", "maria@example.com", "DEU",
        DateOnly.Parse(hired), "Engineer", 60_000m, 40);

    [Fact]
    public async Task Hire_returns_201_with_probation_and_initial_terms()
    {
        (HttpClient client, _) = await OrgAdminClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/workers", Maria());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        WorkerDetailDto detail = (await response.Content.ReadFromJsonAsync<WorkerDetailDto>())!;

        Assert.Equal("Maria Santos", detail.Worker.FullName);
        Assert.Equal(new DateOnly(2027, 2, 1), detail.Worker.ProbationEndsOn); // DEU: 6 months
        Assert.Equal("EUR", detail.CurrentTerms!.SalaryCurrency);              // country currency
        Assert.Equal($"/api/v1/workers/{detail.Worker.Id}",
            response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Terms_as_of_travels_through_history()
    {
        (HttpClient client, _) = await OrgAdminClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/workers", Maria());
        Guid workerId = (await created.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;

        // A raise effective November 1st.
        HttpResponseMessage raise = await client.PostAsJsonAsync(
            $"/api/v1/workers/{workerId}/terms",
            new UpdateTermsRequest(new DateOnly(2026, 11, 1), "Senior Engineer", 75_000m, 40));
        Assert.Equal(HttpStatusCode.Created, raise.StatusCode);

        // September: the original terms govern.
        WorkerTermsDto september = (await client.GetFromJsonAsync<WorkerTermsDto>(
            $"/api/v1/workers/{workerId}/terms?asOf=2026-09-15"))!;
        Assert.Equal(60_000m, september.Effective!.AnnualSalary);

        // December: the raise governs; history holds both records.
        WorkerTermsDto december = (await client.GetFromJsonAsync<WorkerTermsDto>(
            $"/api/v1/workers/{workerId}/terms?asOf=2026-12-01"))!;
        Assert.Equal(75_000m, december.Effective!.AnnualSalary);
        Assert.Equal(2, december.History.Count);
    }

    [Fact]
    public async Task Terms_before_hire_date_are_a_422()
    {
        (HttpClient client, _) = await OrgAdminClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/workers", Maria());
        Guid workerId = (await created.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/workers/{workerId}/terms",
            new UpdateTermsRequest(new DateOnly(2020, 1, 1), "Time Traveler", 1m, 40));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Worker_lists_are_org_isolated()
    {
        (HttpClient clientA, _) = await OrgAdminClientAsync();
        (HttpClient clientB, _) = await OrgAdminClientAsync();

        await clientA.PostAsJsonAsync("/api/v1/workers", Maria());

        List<WorkerDto> aSees = (await clientA.GetFromJsonAsync<List<WorkerDto>>("/api/v1/workers"))!;
        List<WorkerDto> bSees = (await clientB.GetFromJsonAsync<List<WorkerDto>>("/api/v1/workers"))!;

        Assert.Contains(aSees, w => w.FullName == "Maria Santos");
        Assert.DoesNotContain(bSees, w => w.FullName == "Maria Santos");
    }

    [Fact]
    public async Task The_audit_log_tells_the_workers_story()
    {
        (HttpClient client, _) = await OrgAdminClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/workers", Maria());
        Guid workerId = (await created.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;
        await client.PostAsJsonAsync(
            $"/api/v1/workers/{workerId}/terms",
            new UpdateTermsRequest(new DateOnly(2026, 11, 1), "Senior Engineer", 75_000m, 40));

        List<AuditEntryDto> log =
            (await client.GetFromJsonAsync<List<AuditEntryDto>>("/api/v1/audit-log"))!;

        Assert.Contains(log, e => e.Action == "worker.hired" && e.SubjectId == workerId);
        Assert.Contains(log, e => e.Action == "worker.terms_changed" && e.SubjectId == workerId);

        // Before/after payloads make the change reviewable without diffing history.
        AuditEntryDto change = log.First(e => e.Action == "worker.terms_changed");
        Assert.Contains("75000", change.DetailsJson);
    }
}
