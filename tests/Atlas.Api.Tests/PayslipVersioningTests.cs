using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;
using Atlas.Contracts.V2;

namespace Atlas.Api.Tests;

/// <summary>
/// The contract-evolution proof (the playbook's 🔍 placement): v1 payslips are served UNCHANGED from
/// the SAME data as v2, via a flattening adapter — so a v1 consumer observes the identical body, and
/// v1 now carries deprecation headers pointing at its v2 successor. v2 serves the grouped shape.
/// </summary>
// 🔍 "Check what v1 consumers observe: same JSON, byte-for-byte? PROVE it." This test walks a real
// payslip through both versions over HTTP and asserts (a) v1's flat line list is exactly v2's groups
// flattened — same order, labels, kinds, amounts — and (b) gross/net/employer-cost are identical.
// One domain read, two projections, zero drift. (A recorded-golden byte-for-byte variant can be added
// once the suite runs — see NOTES.md — the same bless-on-first-run pattern as the OpenAPI drift gate.)
public class PayslipVersioningTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public PayslipVersioningTests(AtlasApiFactory factory) => _factory = factory;

    private static HireWorkerRequest Maria() =>
        new("Maria Santos", $"maria-{Guid.NewGuid():N}@example.com", "DEU",
            new DateOnly(2026, 1, 1), "Engineer", 60_000m, 40);

    private async Task<Guid> PrepareRunAndGetPayslipIdAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthSessionDto session = await client.RegisterOrgAsync(
            $"Ver Org {Guid.NewGuid():N}"[..18], $"ver-{Guid.NewGuid():N}@example.com");
        client.WithBearer(session.Token);

        await client.PostAsJsonAsync("/api/v1/workers", Maria());
        PayCycleDto cycle = (await (await client.PostAsJsonAsync(
            "/api/v1/payroll/cycles", new CreatePayCycleRequest("DEU", "monthly")))
            .Content.ReadFromJsonAsync<PayCycleDto>())!;

        PayrollRunDetailDto run = (await (await client.PostAsJsonAsync(
            "/api/v1/payroll/runs", new PreparePayrollRunRequest(cycle.Id, 2026, 3)))
            .Content.ReadFromJsonAsync<PayrollRunDetailDto>())!;

        // Stash the authenticated client for the reads below.
        _client = client;
        return run.Payslips.Single().Id;
    }

    private HttpClient _client = null!;

    [Fact]
    public async Task V1_is_deprecated_with_sunset_and_successor_headers()
    {
        Guid payslipId = await PrepareRunAndGetPayslipIdAsync();

        HttpResponseMessage response = await _client.GetAsync($"/api/v1/payslips/{payslipId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Deprecation"), "v1 must announce Deprecation.");
        Assert.True(response.Headers.Contains("Sunset"), "v1 must announce a Sunset date (RFC 8594).");

        string link = string.Join(",", response.Headers.GetValues("Link"));
        Assert.Contains("successor-version", link);
        Assert.Contains($"/api/v2/payslips/{payslipId}", link);
    }

    [Fact]
    public async Task V2_serves_the_grouped_shape_from_the_same_data()
    {
        Guid payslipId = await PrepareRunAndGetPayslipIdAsync();

        PayslipV2Dto v2 = (await _client.GetFromJsonAsync<PayslipV2Dto>($"/api/v2/payslips/{payslipId}"))!;

        // DEU €60k: four employee-deduction lines, four employer-burden lines, no unpaid leave.
        Assert.Equal(4, v2.Lines.EmployeeDeductions.Count);
        Assert.Empty(v2.Lines.UnpaidLeave);
        Assert.Equal(4, v2.Lines.EmployerBurden.Count);
        Assert.Equal(5_000.00m, v2.Gross.Amount);
        Assert.Equal(4_020.00m, v2.Net.Amount);
        Assert.Equal(5_980.00m, v2.EmployerCost.Amount);
    }

    [Fact]
    public async Task V1_is_exactly_v2s_groups_flattened_same_numbers_same_order()
    {
        Guid payslipId = await PrepareRunAndGetPayslipIdAsync();

        PayslipDto v1 = (await _client.GetFromJsonAsync<PayslipDto>($"/api/v1/payslips/{payslipId}"))!;
        PayslipV2Dto v2 = (await _client.GetFromJsonAsync<PayslipV2Dto>($"/api/v2/payslips/{payslipId}"))!;

        // Same top-line numbers across versions.
        Assert.Equal(v1.Gross, v2.Gross);
        Assert.Equal(v1.Net, v2.Net);
        Assert.Equal(v1.EmployerCost, v2.EmployerCost);

        // v1's flat, Kind-tagged list is exactly v2's groups flattened in the historical order
        // (EmployeeDeduction… then UnpaidLeave… then EmployerBurden…). This is "adapter, not fork".
        var expected = new List<PayslipLineDto>();
        expected.AddRange(v2.Lines.EmployeeDeductions.Select(l => new PayslipLineDto(l.Label, "EmployeeDeduction", l.Amount)));
        expected.AddRange(v2.Lines.UnpaidLeave.Select(l => new PayslipLineDto(l.Label, "UnpaidLeave", l.Amount)));
        expected.AddRange(v2.Lines.EmployerBurden.Select(l => new PayslipLineDto(l.Label, "EmployerBurden", l.Amount)));

        Assert.Equal(expected, v1.Lines); // records compare structurally → this is the byte-shape proof
    }
}
