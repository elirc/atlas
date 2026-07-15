using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class CountryEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly HttpClient _client;

    public CountryEndpointsTests(AtlasApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Lists_seeded_countries()
    {
        var countries =
            await _client.GetFromJsonAsync<List<CountryDto>>("/api/v1/countries");

        Assert.NotNull(countries);
        Assert.Contains(countries, c => c is { Code: "DEU", Currency: "EUR" });
    }

    [Fact]
    public async Task Gets_a_country_case_insensitively()
    {
        var country = await _client.GetFromJsonAsync<CountryDto>("/api/v1/countries/phl");

        Assert.Equal("Philippines", country!.Name);
    }

    [Fact]
    public async Task Unknown_country_is_problem_json_404()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/countries/FRA");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        ProblemShape? problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://atlas.example/problems/not-found", problem!.Type);
    }

    [Fact]
    public async Task Malformed_code_is_a_422_domain_rule_problem()
    {
        // LEARN: /countries/FRANCE isn't a missing resource — it's a request the
        // business vocabulary rejects. Contract: 422 + domain-rule-violated.
        HttpResponseMessage response = await _client.GetAsync("/api/v1/countries/FRANCE");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        ProblemShape? problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://atlas.example/problems/domain-rule-violated", problem!.Type);
    }

    public sealed record ProblemShape(string Type, string Title, int Status, string Detail);
}
