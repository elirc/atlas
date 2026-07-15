using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Workforce;

public class WorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    private static Worker SomeWorker(string country = "DEU", string hired = "2026-03-01") =>
        Worker.Hire(
            Guid.NewGuid(), "Maria Santos", EmailAddress.From("maria@example.com"),
            CountryCode.From(country), DateOnly.Parse(hired), Now);

    [Fact]
    public void Hire_trims_name_and_assigns_identity()
    {
        var worker = Worker.Hire(
            Guid.NewGuid(), "  Maria Santos  ", EmailAddress.From("maria@example.com"),
            CountryCode.From("PHL"), new DateOnly(2026, 3, 1), Now);

        Assert.Equal("Maria Santos", worker.FullName);
        Assert.NotEqual(Guid.Empty, worker.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hire_rejects_missing_name(string? name)
    {
        Assert.Throws<DomainRuleException>(() => Worker.Hire(
            Guid.NewGuid(), name, EmailAddress.From("x@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 3, 1), Now));
    }

    [Fact]
    public void Hire_requires_an_org()
    {
        Assert.Throws<DomainRuleException>(() => Worker.Hire(
            Guid.Empty, "Maria", EmailAddress.From("x@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 3, 1), Now));
    }

    [Theory]
    [InlineData("DEU", "2026-03-01", "2026-09-01")] // 6 months
    [InlineData("PHL", "2026-03-01", "2026-09-01")] // 6 months
    [InlineData("GBR", "2026-03-01", "2026-06-01")] // 3 months
    [InlineData("USA", "2026-01-31", "2026-04-30")] // 3 months; Apr 31 doesn't exist → calendar clamps
    public void Probation_end_derives_from_country_rules(
        string country, string hired, string expectedEnd)
    {
        Assert.Equal(DateOnly.Parse(expectedEnd), SomeWorker(country, hired).ProbationEndsOn);
    }

    [Fact]
    public void Unknown_countries_fall_back_to_three_months()
    {
        Assert.Equal(3, CountryEmploymentRules.ProbationMonthsFor(CountryCode.From("FRA")));
    }

    // ── The harvest: probation is a PURE calendar question (ADR-0007) ────────────
    //
    // These cases are the SPEC of the bug that was fixed this sprint: a PHL worker hired
    // on the last day of the month, whose probation window closes on the last day six
    // months later. The predecessor of IsInProbationOn read the server's local clock, so
    // the boundary landed a day early for a client whose calendar day had already turned.
    // Here the boundary is exact and timezone-free — the date is the input.
    [Theory]
    [InlineData("PHL", "2026-01-31", "2026-07-30", true)]  // window closes 2026-07-31; the 30th is still inside
    [InlineData("PHL", "2026-01-31", "2026-07-31", false)] // the last day: probation has ended
    [InlineData("PHL", "2026-01-31", "2026-08-01", false)]
    [InlineData("USA", "2026-01-31", "2026-04-29", true)]  // window closes 2026-04-30 (Apr 31 clamps)
    [InlineData("USA", "2026-01-31", "2026-04-30", false)]
    public void IsInProbationOn_is_a_pure_calendar_question(
        string country, string hired, string date, bool expected)
    {
        Assert.Equal(expected, SomeWorker(country, hired).IsInProbationOn(DateOnly.Parse(date)));
    }
}
