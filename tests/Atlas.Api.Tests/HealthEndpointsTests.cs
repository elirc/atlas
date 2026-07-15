using System.Net;
using System.Net.Http.Json;
using Atlas.Api.Tests.Infrastructure;
using Atlas.Application.Abstractions;
using Atlas.Application.Operations;
using Atlas.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atlas.Api.Tests;

/// <summary>
/// The operational surface: liveness is a trivial always-up signal; readiness reports the database
/// and the outbox dispatcher, and 503s only when a GATING check is down. The last test swaps the
/// public <see cref="IReadinessProbe"/> for a not-ready stub — proving the 503 contract without
/// having to actually sever the database mid-suite.
/// </summary>
public class HealthEndpointsTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public HealthEndpointsTests(AtlasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_is_200_and_needs_no_authentication()
    {
        // No bearer token: liveness must answer for an orchestrator that never logs in.
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HealthDto? body = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.Equal("live", body!.Status);
    }

    [Fact]
    public async Task Every_response_echoes_a_correlation_id()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/health/live");

        // The telemetry middleware wraps the whole pipeline, so even an anonymous health check comes
        // back correlated — the id you would grep the logs for.
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task An_inbound_correlation_id_is_honored()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "trace-abc-123");

        HttpResponseMessage response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal("trace-abc-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Readiness_is_200_and_reports_database_and_outbox_when_healthy()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ReadinessDto? body = await response.Content.ReadFromJsonAsync<ReadinessDto>();
        Assert.Equal("ready", body!.Status);
        Assert.Contains(body.Checks, c => c is { Name: "database", Status: "ok" });
        Assert.Contains(body.Checks, c => c.Name == "outbox-dispatcher");
    }

    [Fact]
    public async Task Readiness_is_503_when_a_gating_check_is_down()
    {
        WebApplicationFactory<Program> app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReadinessProbe>();
                services.AddScoped<IReadinessProbe, NotReadyProbe>();
            }));

        HttpResponseMessage response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        ReadinessDto? body = await response.Content.ReadFromJsonAsync<ReadinessDto>();
        Assert.Equal("not-ready", body!.Status);
        Assert.Contains(body.Checks, c => c.Status == "down");
    }

    private sealed class NotReadyProbe : IReadinessProbe
    {
        public Task<ReadinessReport> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ReadinessReport(
                IsReady: false,
                Checks: [new ReadinessCheck("database", "down", "connection refused (test)")]));
    }
}
