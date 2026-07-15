using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The authorization matrix for the quote endpoints — persona × endpoint → status. Extends
/// the AuthzMatrixTests style (CONTRIBUTING rule: every new endpoint adds its rows). The
/// three personas the factory can mint are anonymous, an org admin, and platform ops;
/// role-vs-role distinctions (admin vs employee) are pinned at the Application layer
/// (CreateQuoteTests), because there is no endpoint yet to add a non-admin member.
/// </summary>
public class QuoteAuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;

    private string _orgAdminToken = null!;
    private string _opsToken = null!;
    private Guid _ownQuoteId;
    private Guid _foreignQuoteId;

    public QuoteAuthzMatrixTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // xunit builds a fresh instance per test → unique emails per run.
        HttpClient setup = _factory.CreateClient();

        var orgA = await setup.RegisterOrgAsync("Quote Matrix A", $"qm-a-{Guid.NewGuid():N}@example.com");
        _orgAdminToken = orgA.Token;
        _ownQuoteId = await CreateQuoteAsync(_orgAdminToken);

        var orgB = await setup.RegisterOrgAsync("Quote Matrix B", $"qm-b-{Guid.NewGuid():N}@example.com");
        _foreignQuoteId = await CreateQuoteAsync(orgB.Token);

        _opsToken = await setup.LoginOpsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreateQuoteAsync(string token)
    {
        HttpResponseMessage created = await As(token).PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest("DEU", 55_000m));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<QuoteDto>())!.Id;
    }

    private HttpClient As(string? token)
    {
        HttpClient client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    // ── POST /quotes: org members create; ops (no org) is forbidden; anon 401 ──

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    [InlineData("ops", HttpStatusCode.Forbidden)]
    public async Task Create_quote_authz(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage response = await As(TokenFor(persona)).PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest("DEU", 60_000m));

        Assert.Equal(expected, response.StatusCode);
    }

    // ── GET /quotes (list): org members only; ops (no org) forbidden; anon 401 ──

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    [InlineData("ops", HttpStatusCode.Forbidden)]
    public async Task List_quotes_authz(string? persona, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/quotes")).StatusCode);
    }

    // ── GET /quotes/{id}: own = 200, foreign = 404, anonymous = 401 ──────────

    [Fact]
    public async Task Org_admin_reads_own_quote_but_a_foreign_quote_does_not_exist_for_them()
    {
        HttpClient admin = As(_orgAdminToken);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/v1/quotes/{_ownQuoteId}")).StatusCode);

        // WHY 404 not 403: existence across tenants is need-to-know.
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/quotes/{_foreignQuoteId}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_reads_no_quote()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/quotes/{_ownQuoteId}")).StatusCode);
    }

    private string? TokenFor(string? persona) => persona switch
    {
        null => null,
        "orgAdmin" => _orgAdminToken,
        "ops" => _opsToken,
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };
}
