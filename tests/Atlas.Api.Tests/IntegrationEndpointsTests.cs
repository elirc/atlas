using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Application.Abstractions;
using Atlas.Contracts.V1;
using Atlas.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Atlas.Api.Tests;

/// <summary>
/// The integration surface end to end: register a webhook, let the outbox fan an event out through a
/// (fake) transport into the delivery log, redeliver a failed one; and mint a scoped API key, use it,
/// hit its scope wall, then revoke it.
/// </summary>
public class IntegrationEndpointsTests : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly FakeWebhookTransport _transport = new();
    private string _adminToken = null!;

    public IntegrationEndpointsTests(AtlasApiFactory factory)
    {
        _app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWebhookTransport>();
                services.AddSingleton<IWebhookTransport>(_transport);
            }));
    }

    public async Task InitializeAsync()
    {
        HttpClient setup = _app.CreateClient();
        AuthSessionDto org = await setup.RegisterOrgAsync(
            $"Intg {Guid.NewGuid():N}"[..12], $"intg-{Guid.NewGuid():N}@example.com");
        _adminToken = org.Token;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() => _app.CreateClient().WithBearer(_adminToken);

    private async Task DrainOutboxAsync()
    {
        using IServiceScope scope = _app.Services.CreateScope();
        var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
        await relay.DrainOnceAsync(CancellationToken.None);
    }

    // ---- Webhooks --------------------------------------------------------------------------------

    [Fact]
    public async Task Registering_a_webhook_reveals_the_secret_once_and_then_never_again()
    {
        HttpClient admin = Admin();

        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/webhooks",
            new RegisterWebhookRequest("https://partner.example/hooks", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var registered = (await created.Content.ReadFromJsonAsync<WebhookRegisteredDto>())!;
        Assert.StartsWith("whsec_", registered.Secret);
        Assert.True(registered.Subscription.Active);

        // The list view carries no secret field at all — the type itself makes leakage impossible.
        var list = (await admin.GetFromJsonAsync<List<WebhookSubscriptionDto>>("/api/v1/webhooks"))!;
        Assert.Contains(list, s => s.Id == registered.Subscription.Id);
    }

    [Fact]
    public async Task An_event_fans_out_to_a_subscription_through_the_outbox()
    {
        HttpClient admin = Admin();
        _transport.Succeed = true;

        // Registering a webhook itself emits webhook.subscription.created into the outbox.
        var registered = (await (await admin.PostAsJsonAsync("/api/v1/webhooks",
            new RegisterWebhookRequest("https://partner.example/hooks", null)))
            .Content.ReadFromJsonAsync<WebhookRegisteredDto>())!;

        await DrainOutboxAsync();

        Assert.NotEmpty(_transport.Sent);
        var deliveries = (await admin.GetFromJsonAsync<List<WebhookDeliveryDto>>(
            $"/api/v1/webhooks/deliveries?subscriptionId={registered.Subscription.Id}"))!;
        Assert.Contains(deliveries, d => d.Status == "Delivered");
    }

    [Fact]
    public async Task A_failed_delivery_can_be_redelivered()
    {
        HttpClient admin = Admin();
        _transport.Succeed = false; // the partner endpoint is down

        var registered = (await (await admin.PostAsJsonAsync("/api/v1/webhooks",
            new RegisterWebhookRequest("https://partner.example/hooks", null)))
            .Content.ReadFromJsonAsync<WebhookRegisteredDto>())!;
        await DrainOutboxAsync();

        var failed = (await admin.GetFromJsonAsync<List<WebhookDeliveryDto>>(
            $"/api/v1/webhooks/deliveries?subscriptionId={registered.Subscription.Id}"))!;
        WebhookDeliveryDto row = Assert.Single(failed, d => d.Status == "Failed");
        Assert.Equal(1, row.Attempts);

        // The endpoint recovers; a human redelivers the recorded payload.
        _transport.Succeed = true;
        HttpResponseMessage redelivered = await admin.PostAsync(
            $"/api/v1/webhooks/deliveries/{row.Id}/redeliver", null);
        redelivered.EnsureSuccessStatusCode();

        var after = (await redelivered.Content.ReadFromJsonAsync<WebhookDeliveryDto>())!;
        Assert.Equal("Delivered", after.Status);
        Assert.Equal(2, after.Attempts); // the failed attempt plus the redelivery
    }

    // ---- API keys --------------------------------------------------------------------------------

    private async Task<ApiKeyCreatedDto> CreateKeyAsync(HttpClient admin, params string[] scopes)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest($"key-{Guid.NewGuid():N}"[..10], scopes));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<ApiKeyCreatedDto>())!;
    }

    [Fact]
    public async Task A_scoped_key_authenticates_and_the_list_never_shows_the_secret()
    {
        HttpClient admin = Admin();
        ApiKeyCreatedDto created = await CreateKeyAsync(admin, "read:webhooks");
        Assert.StartsWith("atk_", created.Key);

        // The key authenticates for a read within its scope.
        HttpClient partner = _app.CreateClient().WithBearer(created.Key);
        HttpResponseMessage ok = await partner.GetAsync("/api/v1/webhooks");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // The management list shows a display prefix only — never the raw key.
        var keys = (await admin.GetFromJsonAsync<List<ApiKeyDto>>("/api/v1/api-keys"))!;
        ApiKeyDto listed = Assert.Single(keys, k => k.Id == created.ApiKey.Id);
        Assert.DoesNotContain(created.Key, listed.DisplayPrefix); // prefix is a fragment, not the key
        Assert.StartsWith(listed.DisplayPrefix, created.Key); // the stored prefix is a real prefix of the key
    }

    [Fact]
    public async Task A_key_is_refused_outside_its_scope()
    {
        HttpClient admin = Admin();
        ApiKeyCreatedDto readWorkers = await CreateKeyAsync(admin, "read:workers");

        HttpClient partner = _app.CreateClient().WithBearer(readWorkers.Key);

        // read:workers cannot read webhooks (needs read:webhooks) — a 403, not a 401.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await partner.GetAsync("/api/v1/webhooks")).StatusCode);

        // ...and certainly cannot write api-keys (needs write:api-keys).
        Assert.Equal(HttpStatusCode.Forbidden,
            (await partner.PostAsJsonAsync("/api/v1/api-keys",
                new CreateApiKeyRequest("nope", ["read:workers"]))).StatusCode);
    }

    [Fact]
    public async Task A_revoked_key_stops_authenticating()
    {
        HttpClient admin = Admin();
        ApiKeyCreatedDto created = await CreateKeyAsync(admin, "read:webhooks");
        HttpClient partner = _app.CreateClient().WithBearer(created.Key);

        Assert.Equal(HttpStatusCode.OK, (await partner.GetAsync("/api/v1/webhooks")).StatusCode);

        (await admin.DeleteAsync($"/api/v1/api-keys/{created.ApiKey.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await partner.GetAsync("/api/v1/webhooks")).StatusCode);
    }

    private sealed class FakeWebhookTransport : IWebhookTransport
    {
        public bool Succeed { get; set; } = true;
        public List<(string Url, string Payload, string Secret)> Sent { get; } = [];

        public Task<WebhookSendResult> SendAsync(string url, string payloadJson, string secret, CancellationToken ct)
        {
            Sent.Add((url, payloadJson, secret));
            return Task.FromResult(Succeed
                ? WebhookSendResult.Ok(200)
                : WebhookSendResult.Fail("partner returned 500", 500));
        }
    }
}
