using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The invoicing rows of the authorization matrix: persona × endpoint → expected status.
/// Conceptually these belong in AuthzMatrixTests; they live here to keep S09 additive and avoid
/// rewriting the S02 file (see NOTES.md). The deny cases (401/404) are the point.
/// </summary>
public class InvoiceAuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;
    private string _orgAdminToken = null!;

    public InvoiceAuthzMatrixTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Inv Matrix {Guid.NewGuid():N}"[..18], $"im-{Guid.NewGuid():N}@example.com");
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
    public async Task Contractor_list_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/contractors")).StatusCode);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    public async Task Creating_a_contractor_requires_hr_or_admin(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage response = await As(TokenFor(persona)).PostAsJsonAsync(
            "/api/v1/contractors",
            new CreateContractorRequest("Matrix Contractor", $"mc-{Guid.NewGuid():N}@example.com", "USA", "USD"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Invoice_list_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/invoices")).StatusCode);

    [Fact]
    public async Task Submitting_an_invoice_is_401_for_anonymous_and_201_for_an_admin_with_a_contractor()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await As(null).PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(Guid.NewGuid(), [new InvoiceLineInput("Work", 100m, "USD")]))).StatusCode);

        HttpClient admin = As(_orgAdminToken);
        var contractor = (await (await admin.PostAsJsonAsync(
            "/api/v1/contractors",
            new CreateContractorRequest("Payable", $"pay-{Guid.NewGuid():N}@example.com", "USA", "USD")))
            .Content.ReadFromJsonAsync<ContractorDto>())!;

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id, [new InvoiceLineInput("Work", 100m, "USD")]))).StatusCode);
    }

    [Fact]
    public async Task A_missing_invoice_is_a_404_for_a_member_and_401_for_anonymous()
    {
        Guid unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/invoices/{unknown}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await As(_orgAdminToken).GetAsync($"/api/v1/invoices/{unknown}")).StatusCode);
    }

    [Fact]
    public async Task Paying_is_401_for_anonymous()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).PostAsync($"/api/v1/invoices/{Guid.NewGuid()}/pay", null)).StatusCode);
    }
}
