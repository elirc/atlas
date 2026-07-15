using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Contracts.V1;

namespace Atlas.Api.Tests;

public class AuthFlowTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public AuthFlowTests(AtlasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_login_me_logout_roundtrip()
    {
        HttpClient client = _factory.CreateClient();

        // Register: org + admin + live session, one call.
        AuthSessionDto session = await client.RegisterOrgAsync("Roundtrip Org", "rt@example.com");
        Assert.NotNull(session.User.OrgId);
        Assert.Equal("OrgAdmin", session.User.Role);
        Assert.StartsWith("atls_", session.Token);

        // Me: the token works.
        client.WithBearer(session.Token);
        MeDto me = (await client.GetFromJsonAsync<MeDto>("/api/v1/auth/me"))!;
        Assert.Equal("rt@example.com", me.Email);
        Assert.Equal(session.User.OrgId, me.OrgId);

        // Logout: token dies.
        HttpResponseMessage logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // LEARN: after revocation the middleware rejects the PRESENTED token with
        // 401 — a stale credential is an authentication failure, never silently
        // downgraded to anonymous.
        HttpResponseMessage meAfter = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Fresh_login_works_and_wrong_credentials_are_uniform_401s()
    {
        HttpClient client = _factory.CreateClient();
        await client.RegisterOrgAsync("Login Org", "login@example.com");

        string token = await client.LoginAsync("login@example.com", "a-long-enough-password");
        Assert.StartsWith("atls_", token);

        // Identical failure shape for wrong password vs unknown user — after
        // stripping the per-request traceId, the bodies must match byte-for-byte.
        string bodyWrongPassword = await FailedLoginBody(client, "login@example.com", "nope-nope-nope");
        string bodyUnknownUser = await FailedLoginBody(client, "ghost@example.com", "nope-nope-nope");
        Assert.Equal(StripTraceId(bodyWrongPassword), StripTraceId(bodyUnknownUser));
    }

    [Fact]
    public async Task Anonymous_me_is_401_with_www_authenticate_and_problem_type()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://atlas.example/problems/unauthenticated", problem!.Type);
    }

    [Fact]
    public async Task Garbage_bearer_token_is_401_not_500_and_not_anonymous()
    {
        HttpClient client = _factory.CreateClient().WithBearer("atls_totally_forged");

        // Even on an endpoint that allows anonymous callers, a PRESENTED bad
        // credential fails loudly (countries is public, yet this is 401).
        HttpResponseMessage response = await client.GetAsync("/api/v1/countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_is_a_422_domain_rule()
    {
        HttpClient client = _factory.CreateClient();
        await client.RegisterOrgAsync("Dup Org", "dup@example.com");

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/auth/register-org",
            new RegisterOrgRequest("Dup Org 2", "DUP@example.com", "a-long-enough-password", "Dup"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    private static string StripTraceId(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");

    private static async Task<string> FailedLoginBody(
        HttpClient client, string email, string password)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private sealed record ProblemShape(string Type, string Title, int Status, string Detail);
}
