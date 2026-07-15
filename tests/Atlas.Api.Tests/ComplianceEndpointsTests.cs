using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;
using Xunit;

namespace Atlas.Api.Tests;

/// <summary>
/// Compliance over real HTTP: a DEU worker starts non-compliant (four required documents missing),
/// becomes compliant as they are recorded, and a soon-expiring document surfaces in the org view.
/// The required set comes from the DEU rule PLUGIN — the endpoint knows nothing about which country.
/// </summary>
public class ComplianceEndpointsTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;
    private string _adminToken = null!;

    public ComplianceEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Comp {Guid.NewGuid():N}"[..12], $"comp-{Guid.NewGuid():N}@example.com");
        _adminToken = org.Token;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() => _factory.CreateClient().WithBearer(_adminToken);

    private static async Task<Guid> HireDeuWorkerAsync(HttpClient admin)
    {
        var detail = (await (await admin.PostAsJsonAsync("/api/v1/workers",
            new HireWorkerRequest("Dana Devlin", $"dana-{Guid.NewGuid():N}@example.com", "DEU",
                new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40)))
            .Content.ReadFromJsonAsync<WorkerDetailDto>())!;
        return detail.Worker.Id;
    }

    [Fact]
    public async Task A_new_worker_is_non_compliant_until_every_required_document_is_recorded()
    {
        HttpClient admin = Admin();
        Guid workerId = await HireDeuWorkerAsync(admin);

        WorkerComplianceDto before = (await admin.GetFromJsonAsync<WorkerComplianceDto>(
            $"/api/v1/workers/{workerId}/compliance"))!;
        Assert.Equal("DEU", before.Country);
        Assert.False(before.IsCompliant);
        Assert.All(before.Documents, d => Assert.Equal("Missing", d.State));
        Assert.Equal(4, before.Documents.Count); // DEU requires four

        // Record all four required kinds (identity carries an expiry well in the future).
        await RecordAsync(admin, workerId, "IdentityProof", new DateOnly(2035, 1, 1));
        await RecordAsync(admin, workerId, "TaxRegistration", null);
        await RecordAsync(admin, workerId, "SocialSecurityRegistration", null);
        await RecordAsync(admin, workerId, "HealthInsuranceProof", null);

        WorkerComplianceDto after = (await admin.GetFromJsonAsync<WorkerComplianceDto>(
            $"/api/v1/workers/{workerId}/compliance"))!;
        Assert.True(after.IsCompliant);
    }

    [Fact]
    public async Task A_soon_expiring_document_appears_in_the_org_expiring_view()
    {
        HttpClient admin = Admin();
        Guid workerId = await HireDeuWorkerAsync(admin);

        // An identity doc expiring in ~10 days.
        DateOnly soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        await RecordAsync(admin, workerId, "IdentityProof", soon);

        var expiring = (await admin.GetFromJsonAsync<List<ExpiringDocumentDto>>(
            "/api/v1/compliance/expiring?withinDays=30"))!;
        Assert.Contains(expiring, d => d.WorkerId == workerId && d.Kind == "IdentityProof");
    }

    [Fact]
    public async Task An_unknown_document_kind_is_a_400()
    {
        HttpClient admin = Admin();
        Guid workerId = await HireDeuWorkerAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/v1/workers/{workerId}/documents",
            new RecordDocumentRequest("NotARealKind", "ref-1", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Compliance_for_another_orgs_worker_is_a_404()
    {
        HttpClient admin = Admin();

        HttpClient otherAdmin = _factory.CreateClient();
        AuthSessionDto other = await otherAdmin.RegisterOrgAsync(
            $"Other {Guid.NewGuid():N}"[..12], $"other-{Guid.NewGuid():N}@example.com");
        Guid otherWorker = await HireDeuWorkerAsync(otherAdmin.WithBearer(other.Token));

        HttpResponseMessage response = await admin.GetAsync($"/api/v1/workers/{otherWorker}/compliance");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task RecordAsync(HttpClient admin, Guid workerId, string kind, DateOnly? expiresOn)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/v1/workers/{workerId}/documents",
            new RecordDocumentRequest(kind, $"{kind}-ref", expiresOn));
        response.EnsureSuccessStatusCode();
    }
}
