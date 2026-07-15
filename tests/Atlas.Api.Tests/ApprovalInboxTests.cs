using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The unified approval inbox over HTTP — one GET returning BOTH a pending leave request and a
/// pending expense claim. The end-to-end proof of the shared component's payoff.
/// </summary>
public class ApprovalInboxTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public ApprovalInboxTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Inbox Org {Guid.NewGuid():N}", $"inbox-{Guid.NewGuid():N}@example.com");
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
        return (await response.Content.ReadFromJsonAsync<WorkerDetailDto>())!.Worker.Id;
    }

    [Fact]
    public async Task Inbox_lists_pending_leave_and_expenses_together()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(client);

        await client.PostAsJsonAsync("/api/v1/leave-requests", new SubmitLeaveRequestRequest(
            workerId, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null));
        await client.PostAsJsonAsync("/api/v1/expenses", new SubmitExpenseRequest(
            workerId, new MoneyDto(240m, "USD"), "Travel", null, null, null));

        List<ApprovalInboxItemDto> inbox =
            (await client.GetFromJsonAsync<List<ApprovalInboxItemDto>>("/api/v1/approvals"))!;

        Assert.Equal(2, inbox.Count);
        Assert.Contains(inbox, i => i.Kind == "leave");
        Assert.Contains(inbox, i => i.Kind == "expense");
    }

    [Fact]
    public async Task Inbox_is_org_scoped()
    {
        HttpClient orgA = await AdminClientAsync();
        Guid workerId = await HireWorkerAsync(orgA);
        await orgA.PostAsJsonAsync("/api/v1/expenses", new SubmitExpenseRequest(
            workerId, new MoneyDto(240m, "USD"), "Travel", null, null, null));

        HttpClient orgB = await AdminClientAsync();
        List<ApprovalInboxItemDto> bInbox =
            (await orgB.GetFromJsonAsync<List<ApprovalInboxItemDto>>("/api/v1/approvals"))!;

        Assert.Empty(bInbox);
    }

    [Fact]
    public async Task Inbox_requires_authentication()
    {
        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage response = await anon.GetAsync("/api/v1/approvals");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
