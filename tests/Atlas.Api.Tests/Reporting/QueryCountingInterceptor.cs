using System.Data.Common;
using Atlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Atlas.Api.Tests.Reporting;

/// <summary>
/// Counts SQL round-trips by intercepting every command execution — EF query logging turned into a
/// hard number a test can assert on. This is the instrument that makes "N+1" falsifiable: a perf
/// claim without a before/after count is a vibe, and this is the meter that produces the number.
/// </summary>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);
        return base.NonQueryExecuting(command, eventData, result);
    }
}

/// <summary>
/// Spins up a throwaway AtlasDbContext over an isolated in-memory SQLite database — the same engine
/// the whole Api.Tests suite runs on, so the query counts these tests assert are the counts real
/// SQLite issues. The caller owns disposal of both the context and the connection.
/// </summary>
internal static class ReportingTestDb
{
    public static (AtlasDbContext Db, SqliteConnection Connection) Create(QueryCountingInterceptor? interceptor = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        DbContextOptionsBuilder<AtlasDbContext> builder =
            new DbContextOptionsBuilder<AtlasDbContext>().UseSqlite(connection);

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        var db = new AtlasDbContext(builder.Options);
        db.Database.EnsureCreated();
        return (db, connection);
    }
}
