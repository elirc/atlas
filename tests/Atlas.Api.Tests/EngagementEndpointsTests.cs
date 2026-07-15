using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// End-to-end lifecycle over real HTTP + real EF (SQLite): create → offer → accept →
/// onboard → activate, then prove the Worker exists at its own resource. Plus the
/// illegal-transition 422, the withdraw path, and tenant isolation.
/// </summary>
public class EngagementEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public EngagementEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            "Engage Co", $"{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return client;
    }

    // INTEGRATION SEAM (S4): starts a quote via S4's endpoint. Only the request shape
    // and the "id" field in the response are assumed here — reconcile with the real
    // quote contract at integration (see NOTES.md).
    private static async Task<Guid> CreateQuoteAsync(HttpClient client, string country)
    {
        // Reconciled with the real S04 contract: CreateQuoteRequest(CountryCode, Gross).
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/quotes", new { countryCode = country, gross = 60_000m });
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<EngagementDetailDto> CreateEngagementAsync(
        HttpClient client, Guid quoteId, string role = "Engineer")
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/engagements", new CreateEngagementRequest(quoteId, role, 40m));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<EngagementDetailDto>())!;
    }

    [Fact]
    public async Task Full_lifecycle_activates_and_a_worker_appears_under_workers()
    {
        HttpClient client = await AdminClientAsync();
        Guid quoteId = await CreateQuoteAsync(client, "DEU");

        EngagementDetailDto detail = await CreateEngagementAsync(client, quoteId);
        Guid id = detail.Engagement.Id;
        Assert.Equal("Quote", detail.Engagement.State);
        Assert.Equal(4, detail.Checklist.Count); // Germany adds works-council

        (await client.PostAsJsonAsync($"/api/v1/engagements/{id}/send-offer",
            new SendOfferRequest("Maria Santos", "maria@example.com"))).EnsureSuccessStatusCode();

        HttpResponseMessage accept = await client.PostAsJsonAsync(
            $"/api/v1/engagements/{id}/accept-offer",
            new AcceptOfferRequest(new DateOnly(2026, 8, 1)));
        accept.EnsureSuccessStatusCode();

        // Complete every checklist item (names come from the server — order-agnostic).
        EngagementDetailDto afterAccept = (await accept.Content.ReadFromJsonAsync<EngagementDetailDto>())!;
        foreach (ChecklistItemDto item in afterAccept.Checklist)
        {
            (await client.PostAsync(
                $"/api/v1/engagements/{id}/checklist/{item.Name}/complete", null))
                .EnsureSuccessStatusCode();
        }

        HttpResponseMessage activate = await client.PostAsync($"/api/v1/engagements/{id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        EngagementDetailDto active = (await activate.Content.ReadFromJsonAsync<EngagementDetailDto>())!;
        Assert.Equal("Active", active.Engagement.State);
        Assert.NotNull(active.Engagement.CreatedWorkerId);
        Assert.All(active.Checklist, i => Assert.True(i.Completed));

        // The activation created a real Worker — fetchable at its own resource.
        HttpResponseMessage worker = await client.GetAsync(
            $"/api/v1/workers/{active.Engagement.CreatedWorkerId}");
        Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
    }

    [Fact]
    public async Task An_illegal_transition_is_a_422_problem()
    {
        HttpClient client = await AdminClientAsync();
        Guid quoteId = await CreateQuoteAsync(client, "USA");
        EngagementDetailDto detail = await CreateEngagementAsync(client, quoteId, "Analyst");

        // Accept before an offer was ever sent — a business-rule violation.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/engagements/{detail.Engagement.Id}/accept-offer",
            new AcceptOfferRequest(new DateOnly(2026, 8, 1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Withdraw_after_an_offer_moves_to_withdrawn()
    {
        HttpClient client = await AdminClientAsync();
        Guid quoteId = await CreateQuoteAsync(client, "USA");
        EngagementDetailDto detail = await CreateEngagementAsync(client, quoteId);
        Guid id = detail.Engagement.Id;

        (await client.PostAsJsonAsync($"/api/v1/engagements/{id}/send-offer",
            new SendOfferRequest("Sam Rivera", "sam@example.com"))).EnsureSuccessStatusCode();

        HttpResponseMessage withdraw = await client.PostAsJsonAsync(
            $"/api/v1/engagements/{id}/withdraw",
            new WithdrawEngagementRequest("Candidate accepted elsewhere"));
        withdraw.EnsureSuccessStatusCode();

        EngagementDetailDto final = (await withdraw.Content.ReadFromJsonAsync<EngagementDetailDto>())!;
        Assert.Equal("Withdrawn", final.Engagement.State);
        Assert.NotNull(final.Engagement.WithdrawnAtUtc);
    }

    [Fact]
    public async Task An_engagement_is_invisible_to_another_org()
    {
        HttpClient orgA = await AdminClientAsync();
        Guid quoteId = await CreateQuoteAsync(orgA, "USA");
        EngagementDetailDto detail = await CreateEngagementAsync(orgA, quoteId);

        HttpClient orgB = await AdminClientAsync();

        // 404, not 403 — existence across tenants is need-to-know.
        Assert.Equal(HttpStatusCode.NotFound,
            (await orgB.GetAsync($"/api/v1/engagements/{detail.Engagement.Id}")).StatusCode);

        List<EngagementDto> bsList =
            (await orgB.GetFromJsonAsync<List<EngagementDto>>("/api/v1/engagements"))!;
        Assert.DoesNotContain(bsList, e => e.Id == detail.Engagement.Id);
    }

    // ── authz boundary (canonical rows also belong in AuthzMatrixTests; see NOTES) ──

    [Theory]
    [InlineData("/api/v1/engagements")]
    public async Task Listing_engagements_requires_authentication(string path)
    {
        HttpClient anon = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task An_org_member_lists_their_own_pipeline()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/v1/engagements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
