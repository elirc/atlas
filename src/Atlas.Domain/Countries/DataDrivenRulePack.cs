namespace Atlas.Domain.Countries;

/// <summary>
/// The default <see cref="ICountryRulePack"/> implementation: a country whose rules are pure DATA —
/// a currency, a probation window, a leave minimum, burden/deduction component lists, and required
/// documents, all passed to the constructor. The two code-bearing hooks default to EMPTY, so a
/// data-only country (the common case, and all four v1 countries) is a tiny subclass that just calls
/// this base with its numbers.
/// </summary>
// 🔗 This is exactly S04's "policy as data" bet, paid off as ADR-0005 scheduled: the data table
// became the DEFAULT plugin. A country that never needs code inherits the empty hooks and stays
// data; a country that does (JPN) overrides one hook and nothing else in the system notices.
public abstract class DataDrivenRulePack(
    CountryCode country,
    CurrencyCode currency,
    int probationMonths,
    decimal annualLeaveMinimumDays,
    IReadOnlyList<BurdenComponent> employerBurden,
    IReadOnlyList<DeductionComponent> employeeDeductions,
    IReadOnlyList<RequiredDocument> requiredDocuments) : ICountryRulePack
{
    public CountryCode Country { get; } = country;
    public CurrencyCode Currency { get; } = currency;
    public int ProbationMonths { get; } = probationMonths;
    public decimal AnnualLeaveMinimumDays { get; } = annualLeaveMinimumDays;
    public IReadOnlyList<BurdenComponent> EmployerBurden { get; } = employerBurden;
    public IReadOnlyList<DeductionComponent> EmployeeDeductions { get; } = employeeDeductions;
    public IReadOnlyList<RequiredDocument> RequiredDocuments { get; } = requiredDocuments;

    public virtual IReadOnlyList<StatutoryLine> AdditionalEmployerBurden(decimal annualGross) => [];

    public virtual IReadOnlyList<StatutoryLine> AdditionalEmployeeDeductions(decimal annualGross) => [];
}
