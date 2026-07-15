using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class ExpenseEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public ExpenseEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Expense Org {Guid.NewGuid():N}", $"exp-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return client;
    }

    private static async Task<Guid> HireWorkerAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workers",
            new HireWorkerRequest(
                "Maria Santos", $"m-{Guid.NewGuid():N}@example.com", "USA",
                new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40));

        response.EnsureSuccessStatusCode();
        WorkerDetailDto detail = (await response.Content.ReadFromJsonAsync<WorkerDetailDto>())!;
        return detail.Worker.Id;
    }

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid workerId, decimal amount = 240m, string category = "Travel") =>
        client.PostAsJsonAsync("/api/v1/expenses", new SubmitExpenseRequest(
            workerId, new MoneyDto(amount, "USD"), category,
            "https://receipts.example/abc", "taxi.pdf", "Client visit"));

    [Fact]
    public async Task Submit_approve_and_read_walkthrough()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(client);

        HttpResponseMessage submit = await SubmitAsync(client, workerId);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        ExpenseClaimDto claim = (await submit.Content.ReadFromJsonAsync<ExpenseClaimDto>())!;
        Assert.Equal("Submitted", claim.Status);
        Assert.Equal(240m, claim.Amount.Amount);
        Assert.Equal("USD", claim.Amount.Currency);
        Assert.Equal("Travel", claim.Category);

        HttpResponseMessage approve =
            await client.PostAsync($"/api/v1/expenses/{claim.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        ExpenseClaimDto approved = (await approve.Content.ReadFromJsonAsync<ExpenseClaimDto>())!;
        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.DecidedByUserId);

        ExpenseClaimDto fetched =
            (await client.GetFromJsonAsync<ExpenseClaimDto>($"/api/v1/expenses/{claim.Id}"))!;
        Assert.Equal("Approved", fetched.Status);

        List<ExpenseClaimDto> list =
            (await client.GetFromJsonAsync<List<ExpenseClaimDto>>("/api/v1/expenses"))!;
        Assert.Contains(list, c => c.Id == claim.Id);
    }

    [Fact]
    public async Task Deny_with_a_reason_and_cancel_reach_terminal_states()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(client);

        ExpenseClaimDto toDeny =
            (await (await SubmitAsync(client, workerId)).Content.ReadFromJsonAsync<ExpenseClaimDto>())!;
        HttpResponseMessage deny = await client.PostAsJsonAsync(
            $"/api/v1/expenses/{toDeny.Id}/deny", new DenyExpenseRequest("Missing itemised receipt."));
        Assert.Equal(HttpStatusCode.OK, deny.StatusCode);
        ExpenseClaimDto denied = (await deny.Content.ReadFromJsonAsync<ExpenseClaimDto>())!;
        Assert.Equal("Denied", denied.Status);
        Assert.Equal("Missing itemised receipt.", denied.DecisionReason);

        ExpenseClaimDto toCancel =
            (await (await SubmitAsync(client, workerId)).Content.ReadFromJsonAsync<ExpenseClaimDto>())!;
        HttpResponseMessage cancel = await client.PostAsync($"/api/v1/expenses/{toCancel.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled",
            (await cancel.Content.ReadFromJsonAsync<ExpenseClaimDto>())!.Status);
    }

    [Fact]
    public async Task An_unknown_category_is_a_400()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(client);

        HttpResponseMessage response = await SubmitAsync(client, workerId, category: "Sorcery");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_non_positive_amount_is_a_422()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(client);

        HttpResponseMessage response = await SubmitAsync(client, workerId, amount: 0m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Expense_claims_are_org_isolated()
    {
        HttpClient orgA = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(orgA);
        ExpenseClaimDto claim =
            (await (await SubmitAsync(orgA, workerId)).Content.ReadFromJsonAsync<ExpenseClaimDto>())!;

        HttpClient orgB = await AdminClientAsync();

        // Org B cannot approve Org A's claim — it does not exist for them.
        HttpResponseMessage foreignApprove =
            await orgB.PostAsync($"/api/v1/expenses/{claim.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, foreignApprove.StatusCode);

        // Nor does it appear in Org B's list.
        List<ExpenseClaimDto> bSees =
            (await orgB.GetFromJsonAsync<List<ExpenseClaimDto>>("/api/v1/expenses"))!;
        Assert.DoesNotContain(bSees, c => c.Id == claim.Id);
    }

    [Fact]
    public async Task Submitting_an_expense_requires_authentication()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage response = await anon.PostAsJsonAsync(
            "/api/v1/expenses",
            new SubmitExpenseRequest(Guid.NewGuid(), new MoneyDto(10m, "USD"), "Travel", null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
