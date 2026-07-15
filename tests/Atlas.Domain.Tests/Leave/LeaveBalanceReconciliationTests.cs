using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Leave;

/// <summary>
/// The price the Reviewer extracted for "no stored balance" (see meta/debate.md): if the balance
/// is only ever a fold, prove the fold cannot silently rot. These are property-style invariants —
/// they sweep every month across several years rather than asserting one hand-picked number.
/// </summary>
public class LeaveBalanceReconciliationTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LeavePolicy Policy(decimal annual, decimal cap) =>
        LeavePolicy.Define(
            Guid.NewGuid(), CountryCode.From("USA"), annual, cap,
            statutoryMinimumDays: 0m, LeaveAccrualCadence.Monthly, Guid.NewGuid(), Created);

    // Sweep hire dates × policies × a three-year horizon of month-end "asOf" dates.
    public static IEnumerable<object[]> Cases()
    {
        string[] hires = ["2026-01-01", "2026-01-31", "2026-04-16", "2026-07-01", "2026-12-31"];
        (decimal Annual, decimal Cap)[] policies = [(12m, 12m), (24m, 5m), (30m, 0m), (20m, 10m)];

        foreach (string hire in hires)
        {
            foreach ((decimal annual, decimal cap) in policies)
            {
                yield return [hire, annual, cap];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Accrual_never_exceeds_entitlement_plus_carryover(string hired, decimal annual, decimal cap)
    {
        LeavePolicy policy = Policy(annual, cap);
        var hire = DateOnly.Parse(hired);

        // WHY: carry-in is capped at `cap`, and a single year earns at most `annual`, so at no
        // point can accrued-with-carryover exceed cap + annual. If the fold ever breaks that
        // ceiling, a boundary is being double-counted.
        for (var asOf = hire; asOf <= hire.AddYears(3); asOf = asOf.AddMonths(1))
        {
            decimal accrued = LeaveAccrual.AccruedThrough(policy, hire, asOf);
            Assert.True(
                accrued <= cap + annual + 0.0001m,
                $"Accrued {accrued} exceeded cap+annual ({cap}+{annual}) at {asOf} (hired {hire}).");
            Assert.True(accrued >= 0m, $"Accrued went negative at {asOf}.");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Accrual_is_monotonic_within_a_leave_year(string hired, decimal annual, decimal cap)
    {
        LeavePolicy policy = Policy(annual, cap);
        var hire = DateOnly.Parse(hired);

        // WHY: within a single leave year accrual only ever grows (it can DROP at a year boundary,
        // where the cap forfeits — that is the point, so we only assert monotonicity inside a year).
        decimal previous = 0m;
        int currentYear = hire.Year;
        for (var asOf = hire; asOf <= hire.AddYears(3); asOf = asOf.AddMonths(1))
        {
            if (asOf.Year != currentYear)
            {
                currentYear = asOf.Year;
                previous = LeaveAccrual.AccruedThrough(policy, hire, asOf);
                continue;
            }

            decimal accrued = LeaveAccrual.AccruedThrough(policy, hire, asOf);
            Assert.True(accrued >= previous, $"Accrual went backwards within {currentYear} at {asOf}.");
            previous = accrued;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    public void Balance_reconciles_with_accrued_minus_spent(int spent)
    {
        // The defining identity the "derived, not stored" design rests on: available + spent is
        // exactly what was accrued. If a use case ever wires "spent" wrong, this breaks.
        LeavePolicy policy = Policy(24m, 10m);
        var hire = new DateOnly(2026, 1, 1);
        var asOf = new DateOnly(2027, 6, 30);

        decimal accrued = LeaveAccrual.AccruedThrough(policy, hire, asOf);
        decimal balance = LeaveAccrual.BalanceOn(policy, hire, asOf, spent);

        Assert.Equal(accrued, balance + spent);
    }
}
