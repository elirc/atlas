using Atlas.Application.Reporting;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;
using Atlas.Infrastructure.Persistence.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Tests.Reporting;

/// <summary>
/// The N+1 hunt as an executable proof. It seeds an org, then measures TWO ways of building the same
/// compliance rollup: the naive per-worker read (1+N round-trips) and the projection
/// (a constant 2). The count is asserted, not eyeballed — so a future edit that reintroduces the
/// per-row read turns this red instead of quietly re-rotting the endpoint under load.
/// </summary>
// 🔍 A PERF TEST THAT ASSERTS THE MEASUREMENT, NOT A DURATION. It never asks "is it fast?" (flaky,
// machine-dependent) — it asks "how many times did it hit the database?", which is the property that
// actually causes the slowness and is deterministic. The naive count is parameterised by worker
// count and the fast count is not; that asymmetry IS the definition of N+1, encoded as a test.
public class ComplianceRosterQueryCountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(25)]
    public async Task Naive_rollup_is_N_plus_1_while_the_projection_is_a_constant_two(int workerCount)
    {
        var counter = new QueryCountingInterceptor();
        (Atlas.Infrastructure.Persistence.AtlasDbContext db, Microsoft.Data.Sqlite.SqliteConnection connection) =
            ReportingTestDb.Create(counter);
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();
            var asOf = new DateOnly(2026, 6, 1);

            for (int i = 0; i < workerCount; i++)
            {
                Worker worker = Worker.Hire(
                    org, $"Worker {i}", EmailAddress.From($"w{i}-{Guid.NewGuid():N}@x.test"),
                    CountryCode.From("DEU"), new DateOnly(2026, 1, 1), Now);
                db.Add(worker);

                // Two documents each, so the single bulk query returns MORE rows than there are
                // workers — proving the fix is "one query for many rows", not "one row per query".
                db.Add(WorkerDocument.Record(org, worker.Id, DocumentKind.IdentityProof, "id-ref", new DateOnly(2030, 1, 1), Now));
                db.Add(WorkerDocument.Record(org, worker.Id, DocumentKind.TaxRegistration, "tax-ref", null, Now));
            }

            await db.SaveChangesAsync();

            // ── RED: the naive rollup — list the workers, then ONE document query per worker. ──────
            counter.Reset();
            List<Worker> listed = await db.Set<Worker>().AsNoTracking()
                .Where(w => w.OrgId == org).ToListAsync();
            foreach (Worker worker in listed)
            {
                // This is exactly IWorkerDocumentRepository.ListForWorkerAsync, reused per row: correct,
                // readable, and 1+N round-trips that grow with every hire.
                _ = await db.Set<WorkerDocument>().AsNoTracking()
                    .Where(d => d.WorkerId == worker.Id && d.OrgId == org).ToListAsync();
            }

            Assert.Equal(workerCount + 1, counter.Count);

            // ── GREEN: the projection — two queries, whatever the worker count. ────────────────────
            counter.Reset();
            var reports = new ReportQueries(db);
            IReadOnlyList<WorkerComplianceInput> inputs =
                await reports.ActiveWorkersWithDocumentsAsync(org, asOf, CancellationToken.None);

            Assert.Equal(2, counter.Count);

            // Parity: the fast path returns the SAME workers and documents as the naive one.
            Assert.Equal(workerCount, inputs.Count);
            Assert.All(inputs, input => Assert.Equal(2, input.Documents.Count));
        }
    }

    [Fact]
    public async Task The_projection_excludes_workers_hired_after_the_as_of_date()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();

            Worker early = Worker.Hire(org, "Early", EmailAddress.From("early@x.test"),
                CountryCode.From("DEU"), new DateOnly(2026, 1, 1), Now);
            Worker late = Worker.Hire(org, "Late", EmailAddress.From("late@x.test"),
                CountryCode.From("DEU"), new DateOnly(2026, 9, 1), Now);
            db.AddRange(early, late);
            await db.SaveChangesAsync();

            var reports = new ReportQueries(db);
            IReadOnlyList<WorkerComplianceInput> inputs =
                await reports.ActiveWorkersWithDocumentsAsync(org, new DateOnly(2026, 6, 1), CancellationToken.None);

            Assert.Single(inputs);
            Assert.Equal(early.Id, inputs[0].WorkerId);
        }
    }
}
