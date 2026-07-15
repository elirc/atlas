using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class PayrollEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public PayrollEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OrgAdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Payroll Org {Guid.NewGuid():N}"[..20], $"pay-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);
        return client;
    }

    private static Task HireDeuWorkerAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/workers", new HireWorkerRequest(
            "Maria Santos", "maria@example.com", "DEU",
            new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40));

    private static async Task<PayCycleDto> CreateDeuCycleAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/cycles", new CreatePayCycleRequest("DEU", "monthly"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PayCycleDto>())!;
    }

    [Fact]
    public async Task Full_payroll_walk_cycle_to_executed_payslip()
    {
        HttpClient client = await OrgAdminClientAsync();
        await HireDeuWorkerAsync(client);
        PayCycleDto cycle = await CreateDeuCycleAsync(client);

        // Prepare March 2026 → 201, one payslip, hand-computed net 4,020.00 EUR.
        HttpResponseMessage prepared = await client.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3));
        Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);

        PayrollRunDetailDto detail = (await prepared.Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;
        Assert.Equal("Draft", detail.Run.State);
        Assert.Single(detail.Payslips);
        Assert.Equal("EUR", detail.TotalNet!.Currency);
        Assert.Equal(4_020.00m, detail.Payslips[0].Net.Amount);

        Guid runId = detail.Run.Id;
        Guid payslipId = detail.Payslips[0].Id;

        // Preparing the same period again is idempotent → 200, same run.
        HttpResponseMessage again = await client.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        PayrollRunDetailDto againDetail = (await again.Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;
        Assert.Equal(runId, againDetail.Run.Id);

        // Walk the state machine through its named endpoints.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/v1/payroll/runs/{runId}/submit-for-review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/v1/payroll/runs/{runId}/approve", null)).StatusCode);

        HttpResponseMessage executed = await client.PostMoneyMoveAsync($"/api/v1/payroll/runs/{runId}/execute");
        Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
        PayrollRunDto executedRun = (await executed.Content.ReadFromJsonAsync<PayrollRunDto>())!;
        Assert.Equal("Executed", executedRun.State);
        Assert.NotNull(executedRun.ExecutedAtUtc);

        // Re-executing with a DIFFERENT idempotency key genuinely re-enters the use case — which is
        // exactly how we prove the two idempotency layers AGREE rather than fight: the S10 middleware
        // dedupes identical retries, and underneath, S07's own natural-key guard makes a re-execute of
        // an already-executed run a no-op that still returns 200.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostMoneyMoveAsync($"/api/v1/payroll/runs/{runId}/execute")).StatusCode);

        // The payslip reads back with its full line detail.
        PayslipDto slip = (await client.GetFromJsonAsync<PayslipDto>($"/api/v1/payslips/{payslipId}"))!;
        Assert.Equal(5_000.00m, slip.Gross.Amount);
        Assert.Equal(4_020.00m, slip.Net.Amount);
        Assert.Contains(slip.Lines, l => l.Kind == "EmployeeDeduction");
        Assert.Contains(slip.Lines, l => l.Kind == "EmployerBurden");
    }

    [Fact]
    public async Task Executing_a_draft_run_is_a_422()
    {
        HttpClient client = await OrgAdminClientAsync();
        await HireDeuWorkerAsync(client);
        PayCycleDto cycle = await CreateDeuCycleAsync(client);

        PayrollRunDetailDto detail = (await (await client.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3)))
            .Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;

        HttpResponseMessage response =
            await client.PostMoneyMoveAsync($"/api/v1/payroll/runs/{detail.Run.Id}/execute");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Runs_are_org_isolated()
    {
        HttpClient clientA = await OrgAdminClientAsync();
        HttpClient clientB = await OrgAdminClientAsync();

        await HireDeuWorkerAsync(clientA);
        PayCycleDto cycle = await CreateDeuCycleAsync(clientA);
        PayrollRunDetailDto detail = (await (await clientA.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3)))
            .Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;

        // Org B cannot see Org A's run: 404, not 403.
        Assert.Equal(HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/api/v1/payroll/runs/{detail.Run.Id}")).StatusCode);

        List<PayrollRunDto> bSees =
            (await clientB.GetFromJsonAsync<List<PayrollRunDto>>("/api/v1/payroll/runs"))!;
        Assert.DoesNotContain(bSees, r => r.Id == detail.Run.Id);
    }

    [Fact]
    public async Task Preparing_a_run_with_no_active_workers_has_null_totals()
    {
        HttpClient client = await OrgAdminClientAsync();
        PayCycleDto cycle = await CreateDeuCycleAsync(client); // no workers hired

        PayrollRunDetailDto detail = (await (await client.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3)))
            .Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;

        Assert.Empty(detail.Payslips);
        Assert.Null(detail.TotalNet); // no payslips → no currency to total in
    }
}
