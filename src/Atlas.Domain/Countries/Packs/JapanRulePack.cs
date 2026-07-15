namespace Atlas.Domain.Countries.Packs;

/// <summary>
/// Japan — the FIRST code-bearing rule pack, and the payoff of S04's debate. Most of Japan's rules
/// are ordinary data (health, pension, employment insurance as rate-on-capped-base components). But
/// one is not: a flat family-support levy that applies only above an earnings floor. A flat AMOUNT
/// conditioned on a threshold is not a rate on a base — the <see cref="BurdenComponent"/> shape
/// cannot express it — so it lives in code, in the one hook designed for exactly this.
/// </summary>
// 📘 The data→code promotion criteria, applied not theorized (S04's Reviewer predicted this exact
// case): promote to a hook ONLY when the value cannot be written as (rate, allowance, cap) on gross.
// A flat amount can't (it isn't a rate). A conditional on/off can't (the shape has no toggle). A rate
// that differs by non-decomposable bands can't. Everything else stays data — note that Japan's health
// and pension below are STILL data; only the levy graduated. The bar is "the shape can't say it,"
// not "this country feels special."
public sealed class JapanRulePack() : DataDrivenRulePack(
    CountryCode.From("JPN"),
    CurrencyCode.From("JPY"),
    probationMonths: 3,
    annualLeaveMinimumDays: 10m,
    employerBurden:
    [
        new BurdenComponent("Health insurance (employer)", 0.0499m, AnnualWageCap: 16_020_000m),
        new BurdenComponent("Employees' pension (employer)", 0.0915m, AnnualWageCap: 7_500_000m),
        new BurdenComponent("Employment insurance (employer)", 0.0095m),
    ],
    employeeDeductions:
    [
        new DeductionComponent("Health insurance (employee)", 0.0499m, AnnualWageCap: 16_020_000m),
        new DeductionComponent("Employees' pension (employee)", 0.0915m, AnnualWageCap: 7_500_000m),
        new DeductionComponent("Employment insurance (employee)", 0.006m),
    ],
    requiredDocuments:
    [
        new RequiredDocument(DocumentKind.IdentityProof, "My Number card or passport", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.WorkAuthorization, "Residence card (Zairyū) / work status", ExpiryTracked: true),
        new RequiredDocument(DocumentKind.SocialSecurityRegistration, "Pension handbook number", ExpiryTracked: false),
    ])
{
    // The threshold and amount are illustrative teaching values, not tax advice.
    private const decimal LevyEarningsFloor = 2_500_000m;
    private const decimal FlatLevyAnnualAmount = 12_000m;

    /// <summary>
    /// The child-rearing / family-support levy: a FLAT ¥12,000/year, but only for workers earning at
    /// least ¥2,500,000. Off below the floor, flat above it — neither a rate nor a base, so it can
    /// only be code. This override is the ENTIRE cost of Japan's special rule; nothing in the
    /// calculators, registry, or API changed to accommodate it.
    /// </summary>
    public override IReadOnlyList<StatutoryLine> AdditionalEmployerBurden(decimal annualGross) =>
        annualGross >= LevyEarningsFloor
            ? [new StatutoryLine("Family-support levy (employer)", FlatLevyAnnualAmount)]
            : [];
}
