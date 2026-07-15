using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Application.Auth;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

/// <summary>
/// End-to-end proof of the S13 login lockout: a run of wrong passwords stops being answered with
/// 401 and becomes 429 (with Retry-After). Uses unique emails per test — the throttle is a process
/// singleton shared across the fixture, so identifiers must not collide between tests.
/// </summary>
public class LoginRateLimitTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public LoginRateLimitTests(AtlasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Repeated_bad_logins_trip_a_429_with_retry_after()
    {
        HttpClient client = _factory.CreateClient();
        string email = $"brute-{Guid.NewGuid():N}@example.com";
        await client.RegisterOrgAsync("Brute Org", email);

        HttpResponseMessage? last = null;
        for (int attempt = 0; attempt <= LoginThrottlePolicy.MaxFailures; attempt++)
        {
            last = await client.PostAsJsonAsync(
                "/api/v1/auth/login", new LoginRequest(email, "definitely-wrong"));
        }

        // The (MaxFailures + 1)th attempt is refused before the password is checked.
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.NotNull(last.Headers.RetryAfter); // RFC 6585 — tell the client how long to wait
    }

    [Fact]
    public async Task Even_the_correct_password_is_refused_while_locked()
    {
        HttpClient client = _factory.CreateClient();
        string email = $"lockme-{Guid.NewGuid():N}@example.com";
        await client.RegisterOrgAsync("Lock Org", email); // registers with the standard test password

        for (int attempt = 0; attempt < LoginThrottlePolicy.MaxFailures; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "definitely-wrong"));
        }

        // The right password now — and it still 429s: the lockout is on the identifier, not the guess.
        HttpResponseMessage locked = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(email, "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_lockout_on_one_account_does_not_touch_another()
    {
        HttpClient client = _factory.CreateClient();
        string victim = $"victim-{Guid.NewGuid():N}@example.com";
        string bystander = $"bystander-{Guid.NewGuid():N}@example.com";
        await client.RegisterOrgAsync("Victim Org", victim);
        await client.RegisterOrgAsync("Bystander Org", bystander);

        for (int attempt = 0; attempt <= LoginThrottlePolicy.MaxFailures; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(victim, "definitely-wrong"));
        }

        // The bystander logs in normally — the throttle is keyed per identifier.
        string token = await client.LoginAsync(bystander, "a-long-enough-password");
        Assert.StartsWith("atls_", token);
    }
}
