using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The expense + approval-inbox rows of the authorization matrix: persona × endpoint → status.
/// They live here to keep S08 additive (not rewriting the S02 AuthzMatrixTests file — see NOTES).
/// The deny cases (401/404) are the point.
/// </summary>
public class ExpenseAuthzMatrixTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;

    private string _orgAdminToken = null!;
    private Guid _workerId;

    public ExpenseAuthzMatrixTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Exp Matrix {Guid.NewGuid():N}"[..18], $"em-{Guid.NewGuid():N}@example.com");
        _orgAdminToken = org.Token;

        setup.WithBearer(_orgAdminToken);
        HttpResponseMessage hire = await setup.PostAsJsonAsync(
            "/api/v1/workers",
            new HireWorkerRequest(
                "Maria Santos", $"m-{Guid.NewGuid():N}@example.com", "USA",
                new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40));
        hire.EnsureSuccessStatusCode();
        _workerId = (await hire.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;
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
    public async Task Expense_list_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/expenses")).StatusCode);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.OK)]
    public async Task Approval_inbox_requires_an_org_member(string? persona, HttpStatusCode expected) =>
        Assert.Equal(expected, (await As(TokenFor(persona)).GetAsync("/api/v1/approvals")).StatusCode);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orgAdmin", HttpStatusCode.Created)]
    public async Task Submitting_an_expense_requires_an_org_member(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage response = await As(TokenFor(persona)).PostAsJsonAsync(
            "/api/v1/expenses",
            new SubmitExpenseRequest(_workerId, new MoneyDto(240m, "USD"), "Travel", null, null, null));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_expense_is_a_404_for_a_member_and_401_for_anonymous()
    {
        Guid unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await As(null).GetAsync($"/api/v1/expenses/{unknown}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await As(_orgAdminToken).GetAsync($"/api/v1/expenses/{unknown}")).StatusCode);
    }
}
