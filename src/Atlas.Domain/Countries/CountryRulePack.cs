namespace Atlas.Domain.Countries;

/// <summary>
/// One employer-side statutory contribution: a named rate on an assessable base,
/// where the base can be floored by an annual allowance and/or capped by an annual
/// ceiling. This is DATA — a row in a table — not code.
/// </summary>
// 📘 The shape carries three real-world patterns with zero branches:
//   • a flat percentage of gross           → Rate set, no allowance, no cap
//   • a percentage up to a wage ceiling     → Cap set (DEU pension, USA social security)
//   • a percentage above a threshold        → Allowance set (GBR employer NI)
// The moment a country needs something this shape CAN'T express (a conditional, a
// bracket, a church-tax opt-in), that is the promotion signal to plugins — recorded
// as the S11 bet in ADR-0005. Until then, data.
public sealed record BurdenComponent(
    string Name,
    decimal Rate,
    decimal? AnnualAllowance = null,
    decimal? AnnualWageCap = null)
{
    /// <summary>
    /// The portion of gross this component's rate actually applies to: capped from
    /// above by the ceiling, then reduced by the allowance, never below zero.
    /// </summary>
    // WHY: pure arithmetic on decimals, no Money here — the caller (QuoteCalculator)
    // owns wrapping this into Money and rounding it, so rounding stays in ONE place.
    public decimal AssessableBase(decimal grossAmount)
    {
        decimal capped = AnnualWageCap is { } cap ? Math.Min(grossAmount, cap) : grossAmount;
        decimal exempt = AnnualAllowance ?? 0m;
        return Math.Max(0m, capped - exempt);
    }
}

/// <summary>
/// One EMPLOYEE-side statutory deduction: the worker's own contribution withheld from
/// gross (pension, health, social security). Same SHAPE as <see cref="BurdenComponent"/>
/// — a rate on an assessable base with an optional allowance/ceiling — but a distinct
/// concept, so it earns a distinct name rather than overloading "burden".
/// </summary>
// 📘 S07 adds this. S04's pack modelled only what the EMPLOYER pays on top of gross
// (cost-of-employment, for quotes). Payroll needs the other half: what comes OUT of the
// worker's gross to reach net. Two lists, one shape, opposite sides of the payslip.
// WHY a parallel type and not `IReadOnlyList<BurdenComponent> EmployeeDeductions`:
// "employer burden" and "employee deduction" read in opposite directions on a payslip;
// the name carries the direction. The arithmetic is identical, on purpose — statutory
// maths does not care whose money it is.
public sealed record DeductionComponent(
    string Name,
    decimal Rate,
    decimal? AnnualAllowance = null,
    decimal? AnnualWageCap = null)
{
    /// <summary>Assessable base — capped, then allowance-reduced, never below zero.</summary>
    public decimal AssessableBase(decimal grossAmount)
    {
        decimal capped = AnnualWageCap is { } cap ? Math.Min(grossAmount, cap) : grossAmount;
        decimal exempt = AnnualAllowance ?? 0m;
        return Math.Max(0m, capped - exempt);
    }
}

/// <summary>
/// Everything Atlas needs to price and time employment in one country, as a single
/// immutable record: its currency, its statutory probation window, the employer burden
/// (S04, for quotes), the statutory leave minimum (S06), and the employee deductions
/// (S07, for payroll). Sprint 04's "policy as data" unit, grown wider each sprint.
/// </summary>
public sealed record CountryRulePack(
    CountryCode Country,
    CurrencyCode Currency,
    int ProbationMonths,
    IReadOnlyList<BurdenComponent> EmployerBurden,
    // 🔗 Same lifecycle as ProbationMonths: the statutory MINIMUM annual paid leave (days).
    // A leave policy may exceed it, never undercut it (LeavePolicy.Define enforces). S06.
    decimal AnnualLeaveMinimumDays,
    // 🔗 The employee side of the payslip: what is withheld from gross to reach net. S07.
    IReadOnlyList<DeductionComponent> EmployeeDeductions);

/// <summary>
/// The registry of rule packs Atlas ships with, v1. Realistic in SHAPE; the rates and
/// ceilings are illustrative teaching values, not tax advice.
/// </summary>
// 🔗 This folds in what Sprint 03's CountryEmploymentRules probation table used to own
// (ProbationMonths now lives on the pack). The old static class still compiles — it now
// delegates here. Watch the lifecycle: S03 shipped a one-column table, S04 grows it into
// a pack, S11 promotes packs to plugins. Same data, three ages.
public static class CountryRulePacks
{
    private static readonly IReadOnlyDictionary<string, CountryRulePack> Packs = Build();

    /// <summary>The pack for a country, or a DomainRuleException (422) if unsupported.</summary>
    public static CountryRulePack For(CountryCode country) =>
        Packs.TryGetValue(country.Value, out CountryRulePack? pack)
            ? pack
            : throw new DomainRuleException($"Atlas has no rule pack for '{country}' yet.");

    /// <summary>Non-throwing lookup — used by the legacy probation shim.</summary>
    public static bool TryFor(CountryCode country, out CountryRulePack pack)
    {
        bool found = Packs.TryGetValue(country.Value, out CountryRulePack? value);
        pack = value!;
        return found;
    }

    public static IReadOnlyCollection<CountryCode> SupportedCountries() =>
        [.. Packs.Values.Select(p => p.Country)];

    private static Dictionary<string, CountryRulePack> Build()
    {
        var packs = new[]
        {
            // Germany — contribution ceilings (Beitragsbemessungsgrenze), illustrative.
            new CountryRulePack(
                CountryCode.From("DEU"), CurrencyCode.From("EUR"), ProbationMonths: 6,
                EmployerBurden:
                [
                    new BurdenComponent("Pension insurance (employer)", 0.093m, AnnualWageCap: 90_600m),
                    new BurdenComponent("Health insurance (employer)", 0.073m, AnnualWageCap: 62_100m),
                    new BurdenComponent("Unemployment insurance (employer)", 0.013m, AnnualWageCap: 90_600m),
                    new BurdenComponent("Long-term care insurance (employer)", 0.017m, AnnualWageCap: 62_100m),
                ],
                AnnualLeaveMinimumDays: 20m,
                EmployeeDeductions:
                [
                    new DeductionComponent("Pension insurance (employee)", 0.093m, AnnualWageCap: 90_600m),
                    new DeductionComponent("Health insurance (employee)", 0.073m, AnnualWageCap: 62_100m),
                    new DeductionComponent("Unemployment insurance (employee)", 0.013m, AnnualWageCap: 90_600m),
                    new DeductionComponent("Long-term care insurance (employee)", 0.017m, AnnualWageCap: 62_100m),
                ]),

            // United Kingdom — employer NI is a rate ABOVE the secondary threshold.
            new CountryRulePack(
                CountryCode.From("GBR"), CurrencyCode.From("GBP"), ProbationMonths: 3,
                EmployerBurden:
                [
                    new BurdenComponent("Employer National Insurance", 0.138m, AnnualAllowance: 9_100m),
                ],
                AnnualLeaveMinimumDays: 28m,
                EmployeeDeductions:
                [
                    new DeductionComponent("Employee National Insurance", 0.08m, AnnualAllowance: 12_570m),
                ]),

            // Philippines — monthly contribution ceilings annualized, illustrative.
            new CountryRulePack(
                CountryCode.From("PHL"), CurrencyCode.From("PHP"), ProbationMonths: 6,
                EmployerBurden:
                [
                    new BurdenComponent("SSS (employer)", 0.095m, AnnualWageCap: 420_000m),
                    new BurdenComponent("PhilHealth (employer)", 0.025m, AnnualWageCap: 1_200_000m),
                    new BurdenComponent("Pag-IBIG (employer)", 0.020m, AnnualWageCap: 120_000m),
                ],
                AnnualLeaveMinimumDays: 5m,
                EmployeeDeductions:
                [
                    new DeductionComponent("SSS (employee)", 0.045m, AnnualWageCap: 420_000m),
                    new DeductionComponent("PhilHealth (employee)", 0.025m, AnnualWageCap: 1_200_000m),
                    new DeductionComponent("Pag-IBIG (employee)", 0.020m, AnnualWageCap: 120_000m),
                ]),

            // United States — FICA social security has a wage base; Medicare is uncapped.
            new CountryRulePack(
                CountryCode.From("USA"), CurrencyCode.From("USD"), ProbationMonths: 3,
                EmployerBurden:
                [
                    new BurdenComponent("Social Security (employer FICA)", 0.062m, AnnualWageCap: 168_600m),
                    new BurdenComponent("Medicare (employer)", 0.0145m),
                    new BurdenComponent("Federal unemployment (FUTA)", 0.006m, AnnualWageCap: 7_000m),
                ],
                AnnualLeaveMinimumDays: 0m,
                EmployeeDeductions:
                [
                    new DeductionComponent("Social Security (employee FICA)", 0.062m, AnnualWageCap: 168_600m),
                    new DeductionComponent("Medicare (employee)", 0.0145m),
                ]),
        };

        return packs.ToDictionary(p => p.Country.Value);
    }
}
