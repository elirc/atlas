using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

public class PayCycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PayCycle Cycle() =>
        PayCycle.Create(Guid.NewGuid(), CountryCode.From("DEU"), PayFrequency.Monthly, T0);

    [Theory]
    [InlineData(2026, 1, "2026-01-01", "2026-01-31")]
    [InlineData(2026, 2, "2026-02-01", "2026-02-28")]  // non-leap February
    [InlineData(2024, 2, "2024-02-01", "2024-02-29")]  // leap February
    [InlineData(2026, 4, "2026-04-01", "2026-04-30")]  // 30-day month
    [InlineData(2026, 12, "2026-12-01", "2026-12-31")]
    public void Monthly_period_spans_the_whole_calendar_month(
        int year, int month, string start, string end)
    {
        PayPeriod period = Cycle().PeriodFor(year, month);

        Assert.Equal(DateOnly.Parse(start), period.Start);
        Assert.Equal(DateOnly.Parse(end), period.End);
        Assert.Equal(month - 1, period.IndexInYear);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void An_impossible_month_is_a_domain_rule_violation(int month) =>
        Assert.Throws<DomainRuleException>(() => PayPeriod.ForMonth(2026, month));

    [Fact]
    public void A_cycle_must_belong_to_an_org() =>
        Assert.Throws<DomainRuleException>(() =>
            PayCycle.Create(Guid.Empty, CountryCode.From("DEU"), PayFrequency.Monthly, T0));
}
