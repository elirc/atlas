using Atlas.Api.Tests.Infrastructure;
using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Infrastructure.Outbox;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Atlas.Api.Tests;

/// <summary>
/// The harvest of the phantom-event flaw (ledger #4, planted S05). Two properties the outbox makes
/// true that publish-before-commit never could: a rolled-back transaction delivers NOTHING, and a
/// committed event is delivered durably — surviving the gap between commit and dispatch.
/// </summary>
public class OutboxDeliveryTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public OutboxDeliveryTests(AtlasApiFactory factory) => _factory = factory;

    private static AtlasEvent Event(string type) =>
        new(type, Guid.NewGuid(), "test", Guid.NewGuid(), """{"n":1}""", new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_rolled_back_transaction_delivers_no_event()
    {
        string type = $"phantom.{Guid.NewGuid():N}";

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            var sink = new OutboxEventSink(db);

            // Publish enqueues an outbox row IN this DbContext — but we never commit. Clearing the
            // change tracker is the rollback: the row was never real.
            await sink.PublishAsync(Event(type), CancellationToken.None);
            db.ChangeTracker.Clear();
        }

        // Nothing to deliver, because nothing was committed. This is the whole fix: no commit, no row.
        var recorder = new RecordingDelivery();
        int dispatched = await DrainAsync(recorder);

        Assert.DoesNotContain(recorder.Delivered, e => e.Type == type);
        using IServiceScope verify = _factory.Services.CreateScope();
        var check = verify.ServiceProvider.GetRequiredService<AtlasDbContext>();
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(m => m.Type == type));
        _ = dispatched;
    }

    [Fact]
    public async Task A_committed_event_is_delivered_exactly_once()
    {
        string type = $"committed.{Guid.NewGuid():N}";

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            await new OutboxEventSink(db).PublishAsync(Event(type), CancellationToken.None);
            await db.SaveChangesAsync(); // the commit — now it is durable
        }

        var recorder = new RecordingDelivery();
        await DrainAsync(recorder);
        await DrainAsync(recorder); // a second drain must NOT redeliver an already-dispatched row

        Assert.Single(recorder.Delivered, e => e.Type == type);
    }

    [Fact]
    public async Task A_failed_delivery_backs_off_and_is_retried_later()
    {
        string type = $"flaky.{Guid.NewGuid():N}";
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            await new OutboxEventSink(db).PublishAsync(Event(type), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // First drain: the endpoint is down. The message is NOT dispatched, its attempt count rises,
        // and its next attempt is pushed into the future (exponential backoff).
        await DrainAsync(new ThrowingDelivery(), clock);
        OutboxMessage row = await SingleAsync(type);
        Assert.Null(row.DispatchedAtUtc);
        Assert.Equal(1, row.Attempts);
        Assert.True(row.NextAttemptAtUtc > clock.Now, "next attempt should be scheduled in the future");

        // A drain at the same instant sees the row as not-yet-due and skips it.
        var recorder = new RecordingDelivery();
        await DrainAsync(recorder, clock);
        Assert.DoesNotContain(recorder.Delivered, e => e.Type == type);

        // Advance past the backoff window: now it is due and the healthy delivery succeeds.
        clock.Now = clock.Now.AddMinutes(1);
        await DrainAsync(recorder, clock);
        Assert.Single(recorder.Delivered, e => e.Type == type);
    }

    private async Task<int> DrainAsync(IEventDelivery delivery, TimeProvider? clock = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        var relay = new OutboxRelay(db, [delivery], clock ?? TimeProvider.System, NullLogger<OutboxRelay>.Instance);
        return await relay.DrainOnceAsync(CancellationToken.None);
    }

    private async Task<OutboxMessage> SingleAsync(string type)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Type == type);
    }

    private sealed class RecordingDelivery : IEventDelivery
    {
        public List<AtlasEvent> Delivered { get; } = [];

        public Task DeliverAsync(AtlasEvent evt, CancellationToken cancellationToken)
        {
            Delivered.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDelivery : IEventDelivery
    {
        public Task DeliverAsync(AtlasEvent evt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("partner endpoint is down");
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
