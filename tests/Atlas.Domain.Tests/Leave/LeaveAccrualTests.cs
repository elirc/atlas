using System.Globalization;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Leave;

public class LeaveAccrualTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // A USA policy (statutory minimum 0) so entitlement and cap are free to be chosen for
    // the arithmetic under test.
    private static LeavePolicy Policy(decimal annual, decimal cap) =>
        LeavePolicy.Define(
            Guid.NewGuid(), CountryCode.From("USA"), annual, cap,
            statutoryMinimumDays: 0m, LeaveAccrualCadence.Monthly, Guid.NewGuid(), Created);

    // annual 12 → 1 day/month, so months read straight off the expected column.
    [Theory]
    // ── Proration on the hire month (actual-days convention) ─────────────────────
    [InlineData("2026-04-01", "2026-04-30", 12, 12, "1")]    // whole 30-day month
    [InlineData("2026-04-16", "2026-04-30", 12, 12, "0.5")]  // 15/30 of April
    [InlineData("2026-04-16", "2026-05-31", 12, 12, "1.5")]  // half April + full May
    [InlineData("2026-01-01", "2026-12-31", 12, 12, "12")]   // a full year
    // ── Carryover cap at the year boundary (cap-then-accrue), cap = 5 ────────────
    [InlineData("2026-01-01", "2027-01-01", 12, 5, "6")]     // 12 → cap 5 → + Jan 2027
    [InlineData("2026-01-01", "2027-12-31", 12, 5, "17")]    // 12 → cap 5 → + full 2027
    [InlineData("2026-01-01", "2028-01-01", 12, 5, "6")]     // capped at each of two boundaries
    [InlineData("2026-07-01", "2027-06-30", 12, 5, "11")]    // mid-year hire, 6 → cap 5 → + Jan..Jun
    // ── asOf before hire earns nothing ──────────────────────────────────────────
    [InlineData("2027-01-01", "2026-12-31", 12, 12, "0")]
    public void AccruedThrough_folds_the_calendar_with_carryover_cap(
        string hired, string asOf, int annual, int cap, string expected)
    {
        LeavePolicy policy = Policy(annual, cap);

        decimal actual = LeaveAccrual.AccruedThrough(
            policy, DateOnly.Parse(hired), DateOnly.Parse(asOf));

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void Hiring_on_the_last_day_earns_a_single_days_slice()
    {
        // Hired Jan 31 (a 31-day month) → 1/31 of January's accrual, nothing more.
        LeavePolicy policy = Policy(annual: 12m, cap: 12m);

        decimal actual = LeaveAccrual.AccruedThrough(
            policy, new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31));

        Assert.Equal(1m / 31m, actual, precision: 10);
    }

    [Theory]
    [InlineData("0", "6")]   // Jan..Jun = 6 accrued, nothing spent
    [InlineData("2", "4")]   // ...minus 2 spent
    [InlineData("6", "0")]   // spent exactly the balance
    public void BalanceOn_subtracts_spent_days(string spent, string expected)
    {
        LeavePolicy policy = Policy(annual: 12m, cap: 12m);

        decimal actual = LeaveAccrual.BalanceOn(
            policy, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
            decimal.Parse(spent, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void The_month_accrues_at_its_start_not_its_end()
    {
        // asOf the 5th of January still credits the whole of January's accrual — the policy
        // choice is "accrue at the start of the month", documented in concepts/date-math.md.
        LeavePolicy policy = Policy(annual: 24m, cap: 24m); // 2 days/month

        decimal earlyJanuary = LeaveAccrual.AccruedThrough(
            policy, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        Assert.Equal(2m, earlyJanuary);
    }
}
