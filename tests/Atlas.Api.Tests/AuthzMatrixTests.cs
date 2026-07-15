using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;

namespace Atlas.Api.Tests;

/// <summary>
/// The authorization matrix: persona × endpoint → expected status. Every new
/// endpoint from Sprint 03 on MUST add its rows here (CONTRIBUTING rule; the
/// hardening sprint audits this table for gaps).
/// </summary>
// LEARN: reviewers read the DENY cases first. The 200s tell you what the feature
// does; the 401/403/404s tell you what the system protects. A matrix like this
// turns "did we remember authz?" from a memory exercise into a diff review.
public class AuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;

    private string _orgAdminToken = null!;
    private string _opsToken = null!;
    private Guid _orgAId;
    private Guid _orgBId;

    public AuthzMatrixTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // GOTCHA: xunit creates a NEW test-class instance per test, so this runs
        // per test even though the factory (class fixture) is shared. Emails must
        // therefore be unique per run — a fixed address 422s on the second test.
        HttpClient setup = _factory.CreateClient();

        var orgA = await setup.RegisterOrgAsync(
            "Matrix Org A", $"matrix-a-{Guid.NewGuid():N}@example.com");
        _orgAdminToken = orgA.Token;
        _orgAId = orgA.User.OrgId!.Value;

        var orgB = await setup.RegisterOrgAsync(
            "Matrix Org B", $"matrix-b-{Guid.NewGuid():N}@example.com");
        _orgBId = orgB.User.OrgId!.Value;

        _opsToken = await setup.LoginOpsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient As(string? token)
    {
        HttpClient client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    // ── Countries: public reference data ────────────────────────────────────

    [Fact]
    public async Task Countries_are_readable_by_everyone()
    {
        Assert.Equal(HttpStatusCode.OK, (await As(null).GetAsync("/api/v1/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(_orgAdminToken).GetAsync("/api/v1/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(_opsToken).GetAsync("/api/v1/countries")).StatusCode);
    }

    // ── Listing all orgs: platform staff only ───────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Forbidden)]
    [InlineData("ops", HttpStatusCode.OK)]
    public async Task Org_list_is_ops_only(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));

        Assert.Equal(expected, (await client.GetAsync("/api/v1/client-orgs")).StatusCode);
    }

    // ── Ops org-creation path ────────────────────────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Forbidden)]
    [InlineData("ops", HttpStatusCode.Created)]
    public async Task Org_creation_is_ops_only(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/client-orgs",
            new Atlas.Contracts.V1.CreateClientOrgRequest($"Created by {persona ?? "anon"}"));

        Assert.Equal(expected, response.StatusCode);
    }

    // ── Reading one org: own = 200, foreign = 404, anonymous = 401 ─────────

    [Fact]
    public async Task Org_admin_reads_own_org_but_foreign_org_does_not_exist_for_them()
    {
        HttpClient admin = As(_orgAdminToken);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/v1/client-orgs/{_orgAId}")).StatusCode);

        // WHY 404 and not 403: existence across tenant boundaries is need-to-know.
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/client-orgs/{_orgBId}")).StatusCode);
    }

    [Fact]
    public async Task Ops_read_any_org_and_anonymous_reads_none()
    {
        Assert.Equal(HttpStatusCode.OK,
            (await As(_opsToken).GetAsync($"/api/v1/client-orgs/{_orgBId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/client-orgs/{_orgAId}")).StatusCode);
    }

    // ── Sprint 03 rows: workers & audit log ─────────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Worker_list_requires_an_org_member(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));

        Assert.Equal(expected, (await client.GetAsync("/api/v1/workers")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    public async Task Hiring_requires_hr_or_admin(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workers",
            new Atlas.Contracts.V1.HireWorkerRequest(
                "Matrix Worker", "mw@example.com", "USA",
                new DateOnly(2026, 8, 1), "Analyst", 50_000m, 40));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Audit_log_requires_hr_or_admin(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));

        Assert.Equal(expected, (await client.GetAsync("/api/v1/audit-log")).StatusCode);
    }

    // ── Sprint 05 rows: engagements ─────────────────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Engagement_list_requires_an_org_member(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));
        Assert.Equal(expected, (await client.GetAsync("/api/v1/engagements")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.NotFound)] // own org, but no such engagement
    public async Task Reading_a_missing_engagement(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));
        Assert.Equal(expected,
            (await client.GetAsync($"/api/v1/engagements/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Creating_an_engagement_requires_authentication()
    {
        HttpResponseMessage response = await As(null).PostAsJsonAsync(
            "/api/v1/engagements",
            new Atlas.Contracts.V1.CreateEngagementRequest(Guid.NewGuid(), "Engineer", 40m));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string? TokenFor(string? persona) => persona switch
    {
        null => null,
        "orgAdmin" => _orgAdminToken,
        "ops" => _opsToken,
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };
}
