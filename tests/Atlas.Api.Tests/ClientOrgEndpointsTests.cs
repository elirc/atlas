using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class ClientOrgEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public ClientOrgEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Ops_create_returns_201_with_location_and_body()
    {
        HttpClient ops = _factory.CreateClient();
        ops.WithBearer(await ops.LoginOpsAsync());

        HttpResponseMessage response = await ops.PostAsJsonAsync(
            "/api/v1/client-orgs", new CreateClientOrgRequest("  Initech GmbH "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        ClientOrgDto? dto = await response.Content.ReadFromJsonAsync<ClientOrgDto>();
        Assert.Equal("Initech GmbH", dto!.Name);
        Assert.Equal($"/api/v1/client-orgs/{dto.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task An_org_admin_fetches_their_own_org_at_its_location()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync("Fetch Co", "fetch@example.com");
        client.WithBearer(session.Token);

        var fetched = await client.GetFromJsonAsync<ClientOrgDto>(
            $"/api/v1/client-orgs/{session.User.OrgId}");

        Assert.Equal("Fetch Co", fetched!.Name);
    }

    [Fact]
    public async Task Blank_name_is_a_422_domain_rule_problem()
    {
        HttpClient ops = _factory.CreateClient();
        ops.WithBearer(await ops.LoginOpsAsync());

        HttpResponseMessage response = await ops.PostAsJsonAsync(
            "/api/v1/client-orgs", new CreateClientOrgRequest("   "));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_org_is_404_even_for_ops()
    {
        HttpClient ops = _factory.CreateClient();
        ops.WithBearer(await ops.LoginOpsAsync());

        HttpResponseMessage response =
            await ops.GetAsync($"/api/v1/client-orgs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Out_of_range_limit_is_400()
    {
        HttpClient ops = _factory.CreateClient();
        ops.WithBearer(await ops.LoginOpsAsync());

        HttpResponseMessage response = await ops.GetAsync("/api/v1/client-orgs?limit=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pagination_walks_the_collection_via_the_header_cursor()
    {
        HttpClient anon = _factory.CreateClient();
        foreach (string name in new[] { "Walk A", "Walk B", "Walk C" })
        {
            await anon.RegisterOrgAsync(name, $"{Guid.NewGuid():N}@example.com");
        }

        HttpClient ops = _factory.CreateClient();
        ops.WithBearer(await ops.LoginOpsAsync());

        var seen = new List<string>();
        string url = "/api/v1/client-orgs?limit=2";

        // LEARN: this loop IS the client contract — follow X-Next-Cursor until it
        // stops appearing. If the header never stops, or items repeat, pagination
        // is broken; this test would hang or the distinct-check would fail.
        for (int hop = 0; hop < 25; hop++)
        {
            HttpResponseMessage page = await ops.GetAsync(url);
            page.EnsureSuccessStatusCode();

            List<ClientOrgDto> items =
                (await page.Content.ReadFromJsonAsync<List<ClientOrgDto>>())!;
            seen.AddRange(items.Select(i => i.Name));

            if (!page.Headers.TryGetValues("X-Next-Cursor", out var cursors))
            {
                break;
            }

            url = $"/api/v1/client-orgs?limit=2&cursor={Uri.EscapeDataString(cursors.Single())}";
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Superset(
            new HashSet<string>(["Walk A", "Walk B", "Walk C"]),
            new HashSet<string>(seen));
    }
}
