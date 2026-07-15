using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;
using Xunit;

namespace Atlas.Api.Tests;

/// <summary>
/// The harvest of the double-pay flaw (ledger #3, planted S09). A client that retries a payment
/// because it never saw the first response must get the ORIGINAL response replayed — not a second
/// charge, and not a confusing "already paid" rejection. The Idempotency-Key middleware is that
/// guarantee; these tests pin the retry semantics the flaw lacked.
/// </summary>
public class IdempotencyTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly AtlasApiFactory _factory;
    private string _adminToken = null!;

    public IdempotencyTests(AtlasApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        HttpClient setup = _factory.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Idem {Guid.NewGuid():N}"[..12], $"idem-{Guid.NewGuid():N}@example.com");
        _adminToken = org.Token;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() => _factory.CreateClient().WithBearer(_adminToken);

    private static async Task<Guid> ApprovedInvoiceAsync(HttpClient admin)
    {
        var contractor = (await (await admin.PostAsJsonAsync("/api/v1/contractors",
            new CreateContractorRequest("Dana Devlin", $"dana-{Guid.NewGuid():N}@example.com", "USA", "USD")))
            .Content.ReadFromJsonAsync<ContractorDto>())!;

        var invoice = (await (await admin.PostAsJsonAsync("/api/v1/invoices",
            new SubmitInvoiceRequest(contractor.Id, [new InvoiceLineInput("Work", 100m, "USD")])))
            .Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;

        (await admin.PostAsync($"/api/v1/invoices/{invoice.Id}/hr-approve", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/v1/invoices/{invoice.Id}/admin-approve", null)).EnsureSuccessStatusCode();
        return invoice.Id;
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid invoiceId, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invoices/{invoiceId}/pay");
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task Paying_without_an_idempotency_key_is_a_400()
    {
        HttpClient admin = Admin();
        Guid invoiceId = await ApprovedInvoiceAsync(admin);

        HttpResponseMessage response = await PayAsync(admin, invoiceId, key: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_retried_pay_with_the_same_key_replays_the_original_response()
    {
        HttpClient admin = Admin();
        Guid invoiceId = await ApprovedInvoiceAsync(admin);
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage first = await PayAsync(admin, invoiceId, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;
        Assert.Equal("Paid", firstBody.Status);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));

        // The retry: same key, same request. It must NOT re-execute — it replays byte-for-byte.
        HttpResponseMessage retry = await PayAsync(admin, invoiceId, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(retry.Headers.Contains("Idempotency-Replayed"));
        var retryBody = (await retry.Content.ReadFromJsonAsync<ContractorInvoiceDto>())!;
        Assert.Equal(firstBody.VendorReference, retryBody.VendorReference); // same charge, not a new one
    }

    [Fact]
    public async Task Without_idempotency_a_second_pay_is_rejected_which_is_what_replay_shields_the_client_from()
    {
        // The contrast that shows the middleware's value: once paid, a FRESH pay (new key) is a 422.
        // The replay above is exactly what protects a retrying client from seeing this confusing error
        // for what is, to them, the same single request.
        HttpClient admin = Admin();
        Guid invoiceId = await ApprovedInvoiceAsync(admin);

        (await PayAsync(admin, invoiceId, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        HttpResponseMessage secondFresh = await PayAsync(admin, invoiceId, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondFresh.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_invoice_is_a_422_conflict()
    {
        HttpClient admin = Admin();
        Guid firstInvoice = await ApprovedInvoiceAsync(admin);
        Guid secondInvoice = await ApprovedInvoiceAsync(admin);
        string key = Guid.NewGuid().ToString();

        (await PayAsync(admin, firstInvoice, key)).EnsureSuccessStatusCode();

        // Same key, different request body/path → a client bug we surface, not a silent wrong replay.
        HttpResponseMessage conflict = await PayAsync(admin, secondInvoice, key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
    }
}
