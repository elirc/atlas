using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// End-to-end quote flow over the real HTTP stack: create → read → list, plus the
/// cross-tenant 404. Money crosses the wire as MoneyDto; the breakdown survives the trip.
/// </summary>
public class QuoteEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public QuoteEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OrgAdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        var session = await client.RegisterOrgAsync("Quote Co", $"quote-{Guid.NewGuid():N}@example.com");
        return client.WithBearer(session.Token);
    }

    [Fact]
    public async Task Create_returns_201_with_the_full_breakdown_and_a_location()
    {
        HttpClient admin = await OrgAdminClientAsync();

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest("DEU", 60_000m));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var dto = (await created.Content.ReadFromJsonAsync<QuoteDto>())!;

        Assert.Equal("DEU", dto.Country);
        Assert.Equal("EUR", dto.Gross.Currency);
        Assert.Equal(60_000.00m, dto.Gross.Amount);

        // 4 statutory DEU lines + the Atlas fee.
        Assert.Equal(5, dto.Lines.Count);
        Assert.Contains(dto.Lines, l => l.Label == "Atlas management fee");

        Assert.Equal("EUR", dto.TotalEmployerCost.Currency);
        Assert.Equal(78_960.00m, dto.TotalEmployerCost.Amount);

        // Converted into the org display currency (USD) at the fixed table rate 1 EUR = 1.08 USD.
        Assert.Equal("USD", dto.DisplayTotal.Currency);
        Assert.Equal(85_276.80m, dto.DisplayTotal.Amount);
    }

    [Fact]
    public async Task A_created_quote_can_be_read_back_by_id()
    {
        HttpClient admin = await OrgAdminClientAsync();

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest("USA", 90_000m));
        var createdDto = (await created.Content.ReadFromJsonAsync<QuoteDto>())!;

        HttpResponseMessage read = await admin.GetAsync($"/api/v1/quotes/{createdDto.Id}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readDto = (await read.Content.ReadFromJsonAsync<QuoteDto>())!;
        Assert.Equal(createdDto.Id, readDto.Id);
        Assert.Equal("USD", readDto.TotalEmployerCost.Currency);
    }

    [Fact]
    public async Task Listing_returns_the_orgs_quotes()
    {
        HttpClient admin = await OrgAdminClientAsync();

        await admin.PostAsJsonAsync("/api/v1/quotes", new CreateQuoteRequest("DEU", 40_000m));
        await admin.PostAsJsonAsync("/api/v1/quotes", new CreateQuoteRequest("USA", 80_000m));

        HttpResponseMessage list = await admin.GetAsync("/api/v1/quotes?limit=25");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var quotes = (await list.Content.ReadFromJsonAsync<List<QuoteDto>>())!;
        Assert.True(quotes.Count >= 2);
    }

    [Fact]
    public async Task One_orgs_quote_is_404_for_another_org()
    {
        HttpClient orgA = await OrgAdminClientAsync();
        HttpClient orgB = await OrgAdminClientAsync();

        var created = (await (await orgA.PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest("DEU", 50_000m)))
            .Content.ReadFromJsonAsync<QuoteDto>())!;

        // orgB cannot even learn that orgA's quote exists.
        HttpResponseMessage cross = await orgB.GetAsync($"/api/v1/quotes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
    }

    [Theory]
    [InlineData("FRA", 60_000)] // supported-code check → unsupported country
    [InlineData("DEU", 0)]      // non-positive gross
    [InlineData("GERMANY", 60_000)] // malformed code
    public async Task Bad_inputs_are_422(string country, decimal gross)
    {
        HttpClient admin = await OrgAdminClientAsync();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/quotes", new CreateQuoteRequest(country, gross));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
