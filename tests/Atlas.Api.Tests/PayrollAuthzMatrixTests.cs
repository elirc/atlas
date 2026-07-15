using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The payroll rows of the authorization matrix: persona × endpoint → expected status.
/// Conceptually these belong in AuthzMatrixTests; they live here to keep S07 additive and
/// avoid rewriting the S02 file (see NOTES.md). The deny cases (401/404) are the point.
/// </summary>
public class PayrollAuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;

    private string _orgAdminToken = null!;

    public PayrollAuthzMatrixTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // GOTCHA: xunit news up the class per test → this runs per test; emails must be
        // unique per run or the second register 422s.
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Pay Matrix {Guid.NewGuid():N}"[..18], $"pm-{Guid.NewGuid():N}@example.com");
        _orgAdminToken = org.Token;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient As(string? token)
    {
        HttpClient client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private string? TokenFor(string? persona) => persona switch
    {
        null => null,
        "orgAdmin" => _orgAdminToken,
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Cycle_list_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/payroll/cycles")).StatusCode);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Run_list_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/payroll/runs")).StatusCode);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    public async Task Creating_a_cycle_requires_an_org_admin(string? persona, HttpStatusCode expected)
    {
        // Fresh org per test → the DEU monthly cycle is never a duplicate here.
        HttpResponseMessage response = await As(TokenFor(persona)).PostAsJsonAsync(
            "/api/v1/payroll/cycles", new CreatePayCycleRequest("DEU", "monthly"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Preparing_a_run_is_401_for_anonymous_and_201_for_an_admin_with_a_cycle()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await As(null).PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(Guid.NewGuid(), 2026, 3))).StatusCode);

        HttpClient admin = As(_orgAdminToken);
        PayCycleDto cycle = (await (await admin.PostAsJsonAsync(
            "/api/v1/payroll/cycles", new CreatePayCycleRequest("DEU", "monthly")))
            .Content.ReadFromJsonAsync<PayCycleDto>())!;

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3))).StatusCode);
    }

    [Fact]
    public async Task A_missing_payslip_is_a_404_for_a_member_and_401_for_anonymous()
    {
        Guid unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/payslips/{unknown}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await As(_orgAdminToken).GetAsync($"/api/v1/payslips/{unknown}")).StatusCode);
    }
}
