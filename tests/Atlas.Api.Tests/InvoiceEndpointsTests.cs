using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// The invoice lifecycle over real HTTP: submit → HR-approve → admin-approve → pay → reconcile.
/// The whole path runs as an OrgAdmin, who is permitted at every step (admin outranks HR on the
/// HR gate); role-specific denials are pinned at the use-case level (InvoiceApprovalTests) and in
/// the authz matrix. Payment goes through the REAL PayGrid mock + ACL adapter — default settings
/// never rate-limit, so the happy path is deterministic.
/// </summary>
public class InvoiceEndpointsTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;
    private string _adminToken = null!;

    public InvoiceEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Inv {Guid.NewGuid():N}"[..12], $"inv-{Guid.NewGuid():N}@example.com");
        _adminToken = org.Token;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() => _factory.CreateClient().WithBearer(_adminToken);

    private static async Task<ContractorDto> CreateContractorAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/contractors",
            new CreateContractorRequest("Dana Devlin", $"dana-{Guid.NewGuid():N}@example.com", "USA", "USD"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContractorDto>())!;
    }

    private static async Task<ContractorInvoiceDto> TransitionAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.PostAsync(url, content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;
    }

    [Fact]
    public async Task Submit_approve_approve_pay_walks_the_invoice_to_paid()
    {
        HttpClient admin = Admin();
        ContractorDto contractor = await CreateContractorAsync(admin);

        HttpResponseMessage submitResponse = await admin.PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id,
            [
                new InvoiceLineInput("Design", 100m, "USD"),
                new InvoiceLineInput("Build", 50m, "USD"),
            ]));

        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var invoice = (await submitResponse.Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;
        Assert.Equal("Submitted", invoice.Status);
        Assert.Equal(150m, invoice.Total.Amount);
        Assert.Equal("USD", invoice.Total.Currency);

        invoice = await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/hr-approve");
        Assert.Equal("HrApproved", invoice.Status);

        invoice = await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/admin-approve");
        Assert.Equal("Approved", invoice.Status);

        invoice = await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/pay");
        Assert.Equal("Paid", invoice.Status);
        Assert.False(string.IsNullOrWhiteSpace(invoice.VendorReference));

        // Reconciliation asks PayGrid where settlement stands (default mock: SETTLED).
        var recon = (await (await admin.GetAsync($"/api/v1/invoices/{invoice.Id}/reconciliation"))
            .Content.ReadFromJsonAsync<InvoiceReconciliationDto>())!;
        Assert.Equal("Settled", recon.Settlement);
    }

    [Fact]
    public async Task Paying_before_both_approvals_is_a_422()
    {
        HttpClient admin = Admin();
        ContractorDto contractor = await CreateContractorAsync(admin);

        var invoice = (await (await admin.PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id, [new InvoiceLineInput("Work", 100m, "USD")])))
            .Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;

        // Straight to pay, skipping both approvals.
        HttpResponseMessage payResponse = await admin.PostAsync($"/api/v1/invoices/{invoice.Id}/pay", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, payResponse.StatusCode);
    }

    [Fact]
    public async Task Submitting_for_an_unknown_contractor_is_a_404()
    {
        HttpResponseMessage response = await Admin().PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(Guid.NewGuid(), [new InvoiceLineInput("Work", 100m, "USD")]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_mixed_currency_invoice_is_a_422()
    {
        HttpClient admin = Admin();
        ContractorDto contractor = await CreateContractorAsync(admin); // USD payout

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id,
            [
                new InvoiceLineInput("Design", 100m, "USD"),
                new InvoiceLineInput("Build", 50m, "EUR"),
            ]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Paid_invoices_are_listable_for_reconciliation()
    {
        HttpClient admin = Admin();
        ContractorDto contractor = await CreateContractorAsync(admin);

        var invoice = (await (await admin.PostAsJsonAsync(
            "/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id, [new InvoiceLineInput("Work", 100m, "USD")])))
            .Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;

        await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/hr-approve");
        await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/admin-approve");
        await TransitionAsync(admin, $"/api/v1/invoices/{invoice.Id}/pay");

        HttpResponseMessage listResponse = await admin.GetAsync("/api/v1/invoices?status=Paid");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var paid = (await listResponse.Content.ReadFromJsonAsync<List<ContractorInvoiceSummaryDto>>())!;
        Assert.Contains(paid, i => i.Id == invoice.Id && i.Status == "Paid");
    }
}
