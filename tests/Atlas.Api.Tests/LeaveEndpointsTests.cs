using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class LeaveEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public LeaveEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Leave Org {Guid.NewGuid():N}", $"leave-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return client;
    }

    private static async Task<Guid> HireUsaWorkerAsync(HttpClient client, string hired = "2026-01-01")
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/workers",
            new HireWorkerRequest(
                "Maria Santos", $"m-{Guid.NewGuid():N}@example.com", "USA",
                DateOnly.Parse(hired), "Engineer", 60_000m, 40));

        response.EnsureSuccessStatusCode();
        WorkerDetailDto detail = (await response.Content.ReadFromJsonAsync<WorkerDetailDto>())!;
        return detail.Worker.Id;
    }

    private static Task<HttpResponseMessage> CreatePolicyAsync(
        HttpClient client, string country = "USA", decimal annual = 24m, decimal cap = 10m) =>
        client.PostAsJsonAsync(
            "/api/v1/leave-policies", new CreateLeavePolicyRequest(country, annual, cap));

    [Fact]
    public async Task Policy_submit_approve_and_balance_walkthrough()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireUsaWorkerAsync(client);

        HttpResponseMessage policyResponse = await CreatePolicyAsync(client);
        Assert.Equal(HttpStatusCode.Created, policyResponse.StatusCode);
        LeavePolicyDto policy = (await policyResponse.Content.ReadFromJsonAsync<LeavePolicyDto>())!;
        Assert.Equal("USA", policy.Country);
        Assert.Equal("Monthly", policy.AccrualCadence);

        // Submit five working days in September (Mon–Fri).
        HttpResponseMessage submit = await client.PostAsJsonAsync(
            "/api/v1/leave-requests",
            new SubmitLeaveRequestRequest(
                workerId, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), "Family visit"));
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        LeaveRequestDto request = (await submit.Content.ReadFromJsonAsync<LeaveRequestDto>())!;
        Assert.Equal("Submitted", request.Status);
        Assert.Equal(5m, request.WorkingDays);

        // Approve.
        HttpResponseMessage approve =
            await client.PostAsync($"/api/v1/leave-requests/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        LeaveRequestDto approved = (await approve.Content.ReadFromJsonAsync<LeaveRequestDto>())!;
        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.DecidedByUserId);

        // Balance reflects the five committed days.
        LeaveBalanceDto balance = (await client.GetFromJsonAsync<LeaveBalanceDto>(
            $"/api/v1/workers/{workerId}/leave-balance?asOf=2026-07-01"))!;
        Assert.Equal(14m, balance.AccruedDays);   // Jan..Jul × 2 days/month
        Assert.Equal(5m, balance.SpentDays);
        Assert.Equal(9m, balance.AvailableDays);
    }

    [Fact]
    public async Task Below_minimum_policy_is_422_naming_the_minimum()
    {
        HttpClient client = await AdminClientAsync();

        // Germany's statutory minimum is 20 days; 10 is below it.
        HttpResponseMessage response = await CreatePolicyAsync(client, "DEU", annual: 10m, cap: 5m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("20", body);
    }

    [Fact]
    public async Task Duplicate_policy_is_422()
    {
        HttpClient client = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await CreatePolicyAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreatePolicyAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Insufficient_balance_blocks_approval()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireUsaWorkerAsync(client);
        await CreatePolicyAsync(client);

        // Jan 5–16 is ten working days; only ~2 days have accrued by then.
        HttpResponseMessage submit = await client.PostAsJsonAsync(
            "/api/v1/leave-requests",
            new SubmitLeaveRequestRequest(
                workerId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16), null));
        LeaveRequestDto request = (await submit.Content.ReadFromJsonAsync<LeaveRequestDto>())!;

        HttpResponseMessage approve =
            await client.PostAsync($"/api/v1/leave-requests/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, approve.StatusCode);
    }

    [Fact]
    public async Task Deny_and_cancel_reach_their_terminal_states()
    {
        HttpClient client = await AdminClientAsync();
        Guid workerId = await HireUsaWorkerAsync(client);
        await CreatePolicyAsync(client);

        Guid toDeny = await SubmitAsync(client, workerId, "2026-09-07", "2026-09-11");
        HttpResponseMessage deny = await client.PostAsync($"/api/v1/leave-requests/{toDeny}/deny", null);
        Assert.Equal(HttpStatusCode.OK, deny.StatusCode);
        Assert.Equal("Denied", (await deny.Content.ReadFromJsonAsync<LeaveRequestDto>())!.Status);

        Guid toCancel = await SubmitAsync(client, workerId, "2026-10-05", "2026-10-09");
        HttpResponseMessage cancel = await client.PostAsync($"/api/v1/leave-requests/{toCancel}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<LeaveRequestDto>())!.Status);
    }

    [Fact]
    public async Task Leave_requests_are_org_isolated()
    {
        HttpClient orgA = await AdminClientAsync();
        Guid workerId = await HireUsaWorkerAsync(orgA);
        await CreatePolicyAsync(orgA);
        Guid requestId = await SubmitAsync(orgA, workerId, "2026-09-07", "2026-09-11");

        HttpClient orgB = await AdminClientAsync();

        // Org B cannot approve Org A's request — it does not exist for them.
        HttpResponseMessage foreignApprove =
            await orgB.PostAsync($"/api/v1/leave-requests/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, foreignApprove.StatusCode);

        // Nor does it appear in Org B's list.
        List<LeaveRequestDto> bSees =
            (await orgB.GetFromJsonAsync<List<LeaveRequestDto>>("/api/v1/leave-requests"))!;
        Assert.DoesNotContain(bSees, r => r.Id == requestId);
    }

    [Fact]
    public async Task Submitting_leave_requires_authentication()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage response = await anon.PostAsJsonAsync(
            "/api/v1/leave-requests",
            new SubmitLeaveRequestRequest(
                Guid.NewGuid(), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> SubmitAsync(HttpClient client, Guid workerId, string from, string to)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leave-requests",
            new SubmitLeaveRequestRequest(workerId, DateOnly.Parse(from), DateOnly.Parse(to), null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeaveRequestDto>())!.Id;
    }
}
