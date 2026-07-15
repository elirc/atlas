using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests.Infrastructure;

public static class AuthHelpers
{
    public static async Task<AuthSessionDto> RegisterOrgAsync(
        this HttpClient client, string orgName, string email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/register-org",
            new RegisterOrgRequest(orgName, email, "a-long-enough-password", "Test Admin"));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>())!;
    }

    public static async Task<string> LoginAsync(
        this HttpClient client, string email, string password)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(email, password));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>())!.Token;
    }

    public static Task<string> LoginOpsAsync(this HttpClient client) =>
        client.LoginAsync(AtlasApiFactory.OpsEmail, AtlasApiFactory.OpsPassword);

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// POST to a money-moving endpoint with a fresh <c>Idempotency-Key</c> (mandatory since S10).
    /// Pass an explicit <paramref name="key"/> to simulate a client retry.
    /// </summary>
    public static Task<HttpResponseMessage> PostMoneyMoveAsync(
        this HttpClient client, string url, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }
}
