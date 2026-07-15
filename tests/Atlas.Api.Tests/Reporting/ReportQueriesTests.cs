using Atlas.Application.Reporting;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;
using Atlas.Infrastructure.Persistence.Reporting;

namespace Atlas.Api.Tests.Reporting;

/// <summary>
/// The read models against real SQLite: the projections group, filter, and fold correctly, and every
/// one is org-scoped at the source. These tests double as the Postgres-dialect audit's SQLite half —
/// if a projection stops translating (a DateOnly compare, an owned-Money column), it fails here first.
/// </summary>
public class ReportQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Headcount_groups_by_country_and_splits_probation_as_of_the_date()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();
            db.Add(Hire(org, "A", "USA", new DateOnly(2026, 1, 1)));
            db.Add(Hire(org, "B", "USA", new DateOnly(2026, 1, 1)));
            db.Add(Hire(org, "C", "DEU", new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();

            IReadOnlyList<HeadcountRow> rows =
                await new ReportQueries(db).HeadcountByCountryAsync(org, new DateOnly(2026, 7, 1), CancellationToken.None);

            HeadcountRow usa = rows.Single(r => r.Country == "USA");
            Assert.Equal(2, usa.Total);
            Assert.Equal(usa.Total, usa.InProbation + usa.Active);

            HeadcountRow deu = rows.Single(r => r.Country == "DEU");
            Assert.Equal(1, deu.Total);
            Assert.Equal(0, deu.InProbation); // hired in 2020 — long past any probation window
        }
    }

    [Fact]
    public async Task Employment_cost_folds_executed_payslips_by_country_and_currency()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();
            Worker worker = Hire(org, "Payee", "DEU", new DateOnly(2026, 1, 1));
            db.Add(worker);

            Payslip payslip = ExecutedRunFor(db, org, worker.Id, "DEU", "EUR");
            await db.SaveChangesAsync();

            IReadOnlyList<EmploymentCostRow> rows =
                await new ReportQueries(db).EmploymentCostByCountryMonthAsync(org, 2026, 3, CancellationToken.None);

            EmploymentCostRow row = Assert.Single(rows);
            Assert.Equal("DEU", row.Country);
            Assert.Equal("EUR", row.Currency);
            Assert.Equal(1, row.WorkerCount);
            // Folded in memory (SQLite cannot SUM decimals) — the totals equal the frozen payslip's.
            Assert.Equal(payslip.Gross.Amount, row.Gross);
            Assert.Equal(payslip.Net.Amount, row.Net);
            Assert.Equal(payslip.EmployerCost.Amount, row.EmployerBurden);
        }
    }

    [Fact]
    public async Task Employment_cost_ignores_runs_that_are_not_executed()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();
            Worker worker = Hire(org, "Payee", "DEU", new DateOnly(2026, 1, 1));
            db.Add(worker);

            // A Draft run with a payslip — real numbers, but not yet trustworthy (not executed).
            var pack = CountryRulePacks.For(CountryCode.From("DEU"));
            var period = PayPeriod.ForMonth(2026, 3);
            var record = EmploymentRecord.Create(Guid.NewGuid(), new DateOnly(2026, 1, 1), "Engineer", 60_000m, "EUR", 40, Guid.NewGuid(), Now);
            var run = PayrollRun.Create(org, Guid.NewGuid(), period, Guid.NewGuid(), PayrollVendor.AtlasNative, Now);
            var calc = PayrollCalculator.Calculate(new PayrollCalculationInput([record], pack, period, 0));
            run.AddPayslip(Payslip.FromCalculation(run.Id, worker.Id, org, period, calc, Now));
            db.Add(run);
            await db.SaveChangesAsync();

            IReadOnlyList<EmploymentCostRow> rows =
                await new ReportQueries(db).EmploymentCostByCountryMonthAsync(org, 2026, 3, CancellationToken.None);

            Assert.Empty(rows);
        }
    }

    [Fact]
    public async Task Renewals_returns_only_expiry_dated_documents_within_the_window_ordered()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid org = Guid.NewGuid();
            Worker worker = Hire(org, "Renewer", "USA", new DateOnly(2026, 1, 1));
            db.Add(worker);
            db.Add(WorkerDocument.Record(org, worker.Id, DocumentKind.WorkAuthorization, "permit-1", new DateOnly(2026, 4, 15), Now));
            db.Add(WorkerDocument.Record(org, worker.Id, DocumentKind.IdentityProof, "id-1", new DateOnly(2030, 1, 1), Now));
            db.Add(WorkerDocument.Record(org, worker.Id, DocumentKind.BankMandate, "bank-1", null, Now));
            await db.SaveChangesAsync();

            IReadOnlyList<UpcomingRenewalRow> rows =
                await new ReportQueries(db).UpcomingRenewalsAsync(org, new DateOnly(2026, 6, 1), CancellationToken.None);

            UpcomingRenewalRow row = Assert.Single(rows); // permit is in-window; far-future & no-expiry excluded
            Assert.Equal("permit-1", row.Reference);
            Assert.Equal(worker.FullName, row.WorkerName);
            Assert.Equal(DocumentKind.WorkAuthorization, row.Kind);
        }
    }

    [Fact]
    public async Task Reports_are_org_scoped_at_the_source()
    {
        var (db, connection) = ReportingTestDb.Create();
        using (db)
        using (connection)
        {
            Guid orgA = Guid.NewGuid();
            Guid orgB = Guid.NewGuid();
            db.Add(Hire(orgA, "A", "USA", new DateOnly(2026, 1, 1)));
            db.Add(Hire(orgB, "B", "USA", new DateOnly(2026, 1, 1)));
            await db.SaveChangesAsync();

            IReadOnlyList<HeadcountRow> rows =
                await new ReportQueries(db).HeadcountByCountryAsync(orgA, new DateOnly(2026, 7, 1), CancellationToken.None);

            Assert.Equal(1, rows.Single(r => r.Country == "USA").Total); // org B's worker is invisible
        }
    }

    private static Worker Hire(Guid org, string name, string country, DateOnly hiredOn) =>
        Worker.Hire(org, name, EmailAddress.From($"{name}-{Guid.NewGuid():N}@x.test"),
            CountryCode.From(country), hiredOn, Now);

    private static Payslip ExecutedRunFor(
        Atlas.Infrastructure.Persistence.AtlasDbContext db, Guid org, Guid workerId, string country, string currency)
    {
        var pack = CountryRulePacks.For(CountryCode.From(country));
        var period = PayPeriod.ForMonth(2026, 3);
        var record = EmploymentRecord.Create(Guid.NewGuid(), new DateOnly(2026, 1, 1), "Engineer", 60_000m, currency, 40, Guid.NewGuid(), Now);
        var run = PayrollRun.Create(org, Guid.NewGuid(), period, Guid.NewGuid(), PayrollVendor.AtlasNative, Now);
        var calc = PayrollCalculator.Calculate(new PayrollCalculationInput([record], pack, period, 0));
        Payslip payslip = Payslip.FromCalculation(run.Id, workerId, org, period, calc, Now);
        run.AddPayslip(payslip);
        run.SubmitForReview();
        run.Approve();
        run.Execute(Now);
        db.Add(run);
        return payslip;
    }
}
