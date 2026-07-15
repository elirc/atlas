using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests.Reporting;

/// <summary>
/// The reporting endpoints over real HTTP: the read models answer through the whole stack, the CSV
/// export streams, and the role/validation gates hold. (Requires the S14 integration wiring — the
/// /reports maps and the reporting DI — from meta/NOTES.md.)
/// </summary>
public class ReportEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public ReportEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Rep {Guid.NewGuid():N}"[..12], $"rep-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return client;
    }

    private static async Task<Guid> HireAsync(HttpClient client, string country)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/workers",
            new HireWorkerRequest($"W {Guid.NewGuid():N}"[..10], $"w-{Guid.NewGuid():N}@example.com",
                country, new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;
    }

    [Fact]
    public async Task Headcount_groups_the_orgs_workers_by_country()
    {
        HttpClient admin = await AdminAsync();
        await HireAsync(admin, "USA");
        await HireAsync(admin, "USA");
        await HireAsync(admin, "DEU");

        List<HeadcountRowDto> rows =
            (await admin.GetFromJsonAsync<List<HeadcountRowDto>>("/api/v1/reports/headcount"))!;

        Assert.Equal(2, rows.Single(r => r.Country == "USA").Total);
        Assert.Equal(1, rows.Single(r => r.Country == "DEU").Total);
    }

    [Fact]
    public async Task Compliance_roster_reports_a_new_worker_as_non_compliant()
    {
        HttpClient admin = await AdminAsync();
        Guid workerId = await HireAsync(admin, "DEU");

        List<ComplianceRosterRowDto> roster =
            (await admin.GetFromJsonAsync<List<ComplianceRosterRowDto>>("/api/v1/reports/compliance-roster"))!;

        ComplianceRosterRowDto row = roster.Single(r => r.WorkerId == workerId);
        Assert.False(row.IsCompliant);
        Assert.Equal(4, row.Outstanding); // DEU requires four documents; none recorded yet
    }

    [Fact]
    public async Task Renewals_csv_streams_a_header_and_the_expiring_document()
    {
        HttpClient admin = await AdminAsync();
        Guid workerId = await HireAsync(admin, "DEU");

        DateOnly soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        await admin.PostAsJsonAsync($"/api/v1/workers/{workerId}/documents",
            new RecordDocumentRequest("IdentityProof", "permit-77", soon));

        HttpResponseMessage response = await admin.GetAsync("/api/v1/reports/renewals.csv");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("worker_id,worker_name,country,kind,reference,expires_on", body);
        Assert.Contains("permit-77", body);
    }

    [Fact]
    public async Task Reports_reject_anonymous_callers()
    {
        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage response = await anon.GetAsync("/api/v1/reports/headcount");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Employment_cost_rejects_an_impossible_month()
    {
        HttpClient admin = await AdminAsync();
        HttpResponseMessage response = await admin.GetAsync("/api/v1/reports/employment-cost?year=2026&month=13");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Meta_countries_lists_the_supported_reference_countries()
    {
        HttpClient admin = await AdminAsync();

        List<SupportedCountryDto> countries =
            (await admin.GetFromJsonAsync<List<SupportedCountryDto>>("/api/v1/reports/meta/countries"))!;

        Assert.Contains(countries, c => c.Code == "USA");
        Assert.Contains(countries, c => c.Code == "DEU");
    }
}
