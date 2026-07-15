using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Outbox;

/// <summary>
/// The in-process dispatcher: a hosted background service that drains the outbox on a poll loop.
/// Each tick takes a fresh DI scope (and thus a fresh <see cref="OutboxRelay"/> + DbContext), drains
/// one batch, and — if it dispatched a full batch — loops again immediately to clear a backlog before
/// sleeping.
/// </summary>
// LEARN: why a hosted service and not "a real queue"? See ADR-0011. The short version: the outbox
// TABLE is the durability guarantee; the dispatcher is just a pump. A queue would move the
// at-least-once problem, not remove it, and this pump is trivially testable in CI (drive OutboxRelay
// directly). The migration trigger to a real broker (delivery volume, multi-node) is recorded in the
// ADR, and the port that survives that move is the outbox table itself.
internal sealed class OutboxDrainService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<OutboxDrainService> logger) : BackgroundService
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started (poll {Interval}s).", IdlePollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int dispatched;
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                OutboxRelay relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
                dispatched = await relay.DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A drain-level failure (e.g. the DB is down) must not kill the dispatcher — log and
                // keep polling. Individual message failures are already handled inside the relay.
                logger.LogError(ex, "Outbox drain iteration failed; will retry.");
                dispatched = 0;
            }

            // Full batch → likely a backlog, poll again now. Otherwise idle-sleep.
            if (dispatched < OutboxRelay.BatchSize)
            {
                try
                {
                    await Task.Delay(IdlePollInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Outbox dispatcher stopped.");
    }
}
