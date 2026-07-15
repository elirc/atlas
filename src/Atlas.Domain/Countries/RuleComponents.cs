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
// The moment a country needs something this shape CAN'T express (a flat-amount levy, a
// conditional toggle, a bracket that isn't decomposable) — that is the promotion signal
// to a CODE-bearing pack hook (S11's AdditionalEmployerBurden). Until then, data.
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
// 📘 S07 added this. S04's pack modelled only what the EMPLOYER pays on top of gross
// (cost-of-employment, for quotes). Payroll needs the other half: what comes OUT of the
// worker's gross to reach net. Two lists, one shape, opposite sides of the payslip.
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
/// A statutory line COMPUTED by a code-bearing rule pack rather than expressed as a
/// <see cref="BurdenComponent"/>/<see cref="DeductionComponent"/> data row — a flat levy, a
/// bracketed rate, a conditional. The annual amount is already in the pack's currency; the
/// calculator wraps it in Money and (for payroll) period-shares it, exactly like a data line.
/// </summary>
public sealed record StatutoryLine(string Name, decimal AnnualAmount);
