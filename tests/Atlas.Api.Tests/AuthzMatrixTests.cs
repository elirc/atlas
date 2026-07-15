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
//
// 🔍 S13 note: the matrix is necessary but not sufficient. It did not catch AF-3 (the
// cross-org worker read) because the HTTP endpoint 404s via a PAIRED org-scoped terms
// lookup even while the leaky use case returns the row. The matrix row below is a
// regression GUARD (it passes before and after the fix); the genuine exposing test is
// at the use-case boundary in Atlas.Application.Tests (WorkerTenancyTests). Keep both.
public class AuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;

    private string _orgAdminToken = null!;
    private string _orgBAdminToken = null!;
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
        _orgBAdminToken = orgB.Token;
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

    // ── Sprint 13 addition: worker DETAIL by id (AF-3 regression guard) ──────

    [Fact]
    public async Task Worker_detail_by_id_is_org_scoped()
    {
        // Org A hires a worker.
        HttpClient adminA = As(_orgAdminToken);
        HttpResponseMessage hired = await adminA.PostAsJsonAsync(
            "/api/v1/workers",
            new Atlas.Contracts.V1.HireWorkerRequest(
                "Detail Worker", $"dw-{Guid.NewGuid():N}@example.com", "USA",
                new DateOnly(2026, 8, 1), "Analyst", 50_000m, 40));
        Assert.Equal(HttpStatusCode.Created, hired.StatusCode);
        Guid workerId = (await hired.Content
            .ReadFromJsonAsync<Atlas.Contracts.V1.WorkerDetailDto>())!.Worker.Id;

        // Own org (admin) reads the detail.
        Assert.Equal(HttpStatusCode.OK,
            (await adminA.GetAsync($"/api/v1/workers/{workerId}")).StatusCode);

        // WHY 404: AF-3. Another tenant must not confirm the worker exists — the by-id read is
        // org-scoped (GetWorker.FindInOrgAsync), not merely authenticated. This row passed even
        // BEFORE the fix (the endpoint's paired terms lookup 404'd), which is precisely why the
        // matrix missed the leak; it stands here as the standing regression guard.
        Assert.Equal(HttpStatusCode.NotFound,
            (await As(_orgBAdminToken).GetAsync($"/api/v1/workers/{workerId}")).StatusCode);

        // Anonymous is 401.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/workers/{workerId}")).StatusCode);
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

    // ── Sprint 06 rows: leave ────────────────────────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Leave_policy_list_requires_an_org_member(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));
        Assert.Equal(expected, (await client.GetAsync("/api/v1/leave-policies")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    public async Task Creating_leave_policy_is_org_admin_only(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leave-policies",
            new Atlas.Contracts.V1.CreateLeavePolicyRequest("USA", 24m, 10m));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Leave_request_list_requires_hr_or_admin(string? persona, HttpStatusCode expected)
    {
        HttpClient client = As(TokenFor(persona));
        Assert.Equal(expected, (await client.GetAsync("/api/v1/leave-requests")).StatusCode);
    }

    private string? TokenFor(string? persona) => persona switch
    {
        null => null,
        "orgAdmin" => _orgAdminToken,
        "ops" => _opsToken,
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };
}
