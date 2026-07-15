using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Leave;

public class LeavePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Define_accepts_entitlement_at_or_above_the_statutory_minimum()
    {
        var policy = LeavePolicy.Define(
            Org, CountryCode.From("DEU"), 25m, 10m, statutoryMinimumDays: 20m,
            LeaveAccrualCadence.Monthly, User, Now);

        Assert.Equal(25m, policy.AnnualEntitlementDays);
        Assert.Equal(10m, policy.CarryoverCapDays);
        Assert.Equal(LeaveAccrualCadence.Monthly, policy.AccrualCadence);
    }

    [Fact]
    public void Define_rejects_entitlement_below_the_minimum_and_names_it()
    {
        var ex = Assert.Throws<DomainRuleException>(() => LeavePolicy.Define(
            Org, CountryCode.From("DEU"), 15m, 5m, statutoryMinimumDays: 20m,
            LeaveAccrualCadence.Monthly, User, Now));

        Assert.Contains("20", ex.Message);
        Assert.Contains("DEU", ex.Message);
    }

    [Fact]
    public void A_zero_minimum_country_allows_any_positive_entitlement()
    {
        var policy = LeavePolicy.Define(
            Org, CountryCode.From("USA"), 5m, 0m, statutoryMinimumDays: 0m,
            LeaveAccrualCadence.Monthly, User, Now);

        Assert.Equal(5m, policy.AnnualEntitlementDays);
    }

    [Fact]
    public void Define_rejects_a_carryover_cap_above_the_entitlement()
    {
        Assert.Throws<DomainRuleException>(() => LeavePolicy.Define(
            Org, CountryCode.From("USA"), 10m, 15m, statutoryMinimumDays: 0m,
            LeaveAccrualCadence.Monthly, User, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(400)]
    public void Define_rejects_nonsensical_entitlement(int days)
    {
        Assert.Throws<DomainRuleException>(() => LeavePolicy.Define(
            Org, CountryCode.From("USA"), days, 0m, statutoryMinimumDays: 0m,
            LeaveAccrualCadence.Monthly, User, Now));
    }

    [Fact]
    public void Define_requires_an_org()
    {
        Assert.Throws<DomainRuleException>(() => LeavePolicy.Define(
            Guid.Empty, CountryCode.From("USA"), 10m, 0m, 0m,
            LeaveAccrualCadence.Monthly, User, Now));
    }
}
