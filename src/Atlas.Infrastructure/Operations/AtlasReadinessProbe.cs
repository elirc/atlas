using Atlas.Application.Abstractions;
using Atlas.Application.Operations;
using Atlas.Infrastructure.Outbox;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Operations;

/// <summary>
/// The one <see cref="IReadinessProbe"/> implementation. It asks two questions: can we reach the
/// database, and how far behind is the outbox dispatcher? The database is a HARD gate (no DB → 503,
/// drain this node). The outbox lag is REPORTED but never gates — see <see cref="CheckAsync"/>.
/// </summary>
// 📘 The outbox is the S10 seam whose "depth and oldest-undispatched age are the health signals the
//    S15 runbook watches" (ADR-0011, Consequences). This probe is where that promise is kept: it
//    turns the outbox table into the /health/ready signal and the docs/runbooks/outbox-dispatcher-lag.md
//    alert.
internal sealed class AtlasReadinessProbe(
    AtlasDbContext db,
    TimeProvider clock,
    ILogger<AtlasReadinessProbe> logger) : IReadinessProbe
{
    // ⚠️ An OPS KNOB, not a correctness constant. Crossing it flips the reported status to
    //    "degraded" (→ an alert fires); it does NOT flip readiness. Tightening it into a readiness
    //    gate is exactly the mistake that turns a lagging dispatcher into a fleet-wide 503 cascade.
    internal static readonly TimeSpan LagWarn = TimeSpan.FromMinutes(2);

    public async Task<ReadinessReport> CheckAsync(CancellationToken cancellationToken)
    {
        ReadinessCheck database = await CheckDatabaseAsync(cancellationToken);
        ReadinessCheck outbox = await CheckOutboxAsync(cancellationToken);

        // 🔗 The gate is the DATABASE only. Read this next to the ⚠️ in HealthEndpoints and the
        //    health-signal note in ADR-0011: outbox lag is a page for an operator, not a reason to
        //    pull every node out of the load balancer.
        bool ready = database.Status == "ok";
        return new ReadinessReport(ready, [database, outbox]);
    }

    private async Task<ReadinessCheck> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? new ReadinessCheck("database", "ok", "reachable")
                : new ReadinessCheck("database", "down", "cannot open a connection");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Readiness: database check threw.");
            return new ReadinessCheck("database", "down", ex.GetType().Name);
        }
    }

    private async Task<ReadinessCheck> CheckOutboxAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        IQueryable<OutboxMessage> pending = db.Set<OutboxMessage>().Where(m => m.DispatchedAtUtc == null);

        int pendingCount = await pending.CountAsync(cancellationToken);
        int poisoned = await pending.CountAsync(m => m.Attempts >= OutboxMessage.MaxAttempts, cancellationToken);

        // Lag = age of the oldest DUE-but-undelivered row. A row backed off to the future (or parked
        // as poison a century out) is NOT lag — it is waiting on purpose. Measuring the *due* age is
        // what distinguishes "the dispatcher is behind" from "one broken endpoint is retrying
        // politely". Get this wrong and a single dead partner webhook reads as a platform outage.
        DateTimeOffset? oldestDue = await pending
            .Where(m => m.NextAttemptAtUtc <= now)
            .OrderBy(m => m.OccurredAtUtc)
            .Select(m => (DateTimeOffset?)m.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        double lagSeconds = oldestDue is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0;

        string status = lagSeconds >= LagWarn.TotalSeconds || poisoned > 0 ? "degraded" : "ok";
        string detail = $"pending={pendingCount}, oldestDueAgeSeconds={lagSeconds:F0}, poisoned={poisoned}";
        return new ReadinessCheck("outbox-dispatcher", status, detail);
    }
}
