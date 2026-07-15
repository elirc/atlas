namespace Atlas.Domain.Countries;

/// <summary>
/// Everything Atlas needs to price, pay, time, and comply with employment in ONE country — as a
/// PLUGIN. Each country is a class implementing this contract; the registry discovers them. Adding a
/// country is adding a file, never editing core (the S11 thesis, proved with a diff in the PR).
/// </summary>
// 📘 Designing a plugin contract is a balance between two failures:
//   • too NARROW (pure data) and a country with a genuinely code-shaped rule can't be expressed —
//     the wall S04's Reviewer predicted the data table would hit.
//   • too WIDE and every pack must implement hooks it doesn't need, and the contract churns whenever
//     one country wants something new.
// The line drawn here: the common case is DATA (burden/deduction components, leave minimum,
// probation, required documents), and the escape hatch is two narrow, DEFAULT-EMPTY hooks for
// computed statutory lines. A data-only country implements zero hooks (it inherits the empty
// defaults from DataDrivenRulePack); a code-bearing country overrides exactly the one it needs. The
// contract does not grow when a country needs a conditional — the hook was already there.
public interface ICountryRulePack
{
    CountryCode Country { get; }
    CurrencyCode Currency { get; }
    int ProbationMonths { get; }

    /// <summary>Statutory MINIMUM annual paid leave (days). A policy may exceed it, never undercut.</summary>
    decimal AnnualLeaveMinimumDays { get; }

    /// <summary>Employer cost ON TOP of gross (for quotes and payroll reporting).</summary>
    IReadOnlyList<BurdenComponent> EmployerBurden { get; }

    /// <summary>Employee contributions withheld FROM gross to reach net (for payroll).</summary>
    IReadOnlyList<DeductionComponent> EmployeeDeductions { get; }

    /// <summary>Documents this country requires before a worker is compliant (S11 compliance engine).</summary>
    IReadOnlyList<RequiredDocument> RequiredDocuments { get; }

    /// <summary>
    /// Code-bearing employer burden the flat component shape cannot express (a flat levy, a
    /// conditional, a bracket). Computed from the annual gross. Default (data packs): none.
    /// </summary>
    IReadOnlyList<StatutoryLine> AdditionalEmployerBurden(decimal annualGross);

    /// <summary>Code-bearing employee deductions, same idea. Default (data packs): none.</summary>
    IReadOnlyList<StatutoryLine> AdditionalEmployeeDeductions(decimal annualGross);
}
