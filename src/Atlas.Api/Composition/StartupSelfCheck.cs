using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Composition;

/// <summary>
/// A fail-fast boot probe. Before the host starts accepting traffic, verify the one dependency it
/// cannot serve a single request without — the database is reachable. A misconfigured connection
/// string should crash the process AT STARTUP, loud in the deploy logs, not thirty seconds later as
/// a stream of 500s to users while an orchestrator counts a green liveness check as "fine".
/// </summary>
// 🔍 Why this is separate from /health/ready: readiness answers a running node's "should I get
//    traffic?" over and over; the self-check answers a starting node's "should I exist?" exactly
//    once. Same query, different question — and a failed self-check is a non-zero exit, which is the
//    signal a deploy pipeline actually blocks on.
public static class StartupSelfCheck
{
    public static async Task RunAsync(WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();

        if (!await db.Database.CanConnectAsync())
        {
            // A startup exception is intentional: the host does not come up, the deploy fails closed.
            throw new InvalidOperationException(
                "Startup self-check failed: the database is not reachable. " +
                "Check ConnectionStrings:atlas and Database:Provider before serving traffic.");
        }

        app.Logger.LogInformation("Startup self-check passed: database reachable.");
    }
}
