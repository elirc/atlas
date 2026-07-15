using Atlas.Domain.Countries;

namespace Atlas.Domain.Leave;

/// <summary>How often leave is granted.</summary>
// WHY: v1 supports Monthly only — but the cadence is stored as DATA, not hard-coded,
// so adding Weekly or Annual later is a new enum value plus an engine branch, not a
// schema migration. A one-member enum reads oddly today; it is the seam that keeps
// tomorrow's cadence a data change. (Same "policy as data" lifecycle as the rule packs.)
public enum LeaveAccrualCadence
{
    Monthly = 1,
}

/// <summary>
/// One org's paid-leave rules for one country: annual entitlement, accrual cadence, and
/// how many unused days may roll into the next leave year. The entitlement must meet the
/// country's statutory minimum (carried in from the rule pack) — a policy below the legal
/// floor is a compliance defect, refused at construction, not a value we quietly store.
/// </summary>
public sealed class LeavePolicy
{
    public Guid Id { get; }
    public Guid OrgId { get; }
    public CountryCode Country { get; }

    /// <summary>Paid-leave days earned across a full year of employment.</summary>
    public decimal AnnualEntitlementDays { get; }

    public LeaveAccrualCadence AccrualCadence { get; }

    /// <summary>Max unused days carried into the next leave year (0 = use-it-or-lose-it).</summary>
    public decimal CarryoverCapDays { get; }

    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private LeavePolicy(
        Guid id,
        Guid orgId,
        CountryCode country,
        decimal annualEntitlementDays,
        LeaveAccrualCadence accrualCadence,
        decimal carryoverCapDays,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        Country = country;
        AnnualEntitlementDays = annualEntitlementDays;
        AccrualCadence = accrualCadence;
        CarryoverCapDays = carryoverCapDays;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public static LeavePolicy Define(
        Guid orgId,
        CountryCode country,
        decimal annualEntitlementDays,
        decimal carryoverCapDays,
        decimal statutoryMinimumDays,
        LeaveAccrualCadence cadence,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A leave policy must belong to an org.");
        }

        if (annualEntitlementDays is <= 0m or > 366m)
        {
            throw new DomainRuleException("Annual entitlement must be between 1 and 366 days.");
        }

        // WHY: the statutory floor is the whole reason this is a DOMAIN rule and not a
        // form validation — a policy below a country's legal minimum is not a typo we
        // forgive, it is a defect we refuse. The minimum travels IN as a parameter (from
        // the country rule pack) so the message can name it; the Api surfaces it as a 422.
        if (annualEntitlementDays < statutoryMinimumDays)
        {
            throw new DomainRuleException(
                $"Annual entitlement of {annualEntitlementDays:0.##} days is below the statutory " +
                $"minimum of {statutoryMinimumDays:0.##} days for {country}.");
        }

        if (carryoverCapDays < 0m || carryoverCapDays > annualEntitlementDays)
        {
            throw new DomainRuleException(
                "Carryover cap must be between 0 and the annual entitlement.");
        }

        return new LeavePolicy(
            Guid.NewGuid(), orgId, country, annualEntitlementDays, cadence,
            carryoverCapDays, createdByUserId, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private LeavePolicy()
    {
    }
}
