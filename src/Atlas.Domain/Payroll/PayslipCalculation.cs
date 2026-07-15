namespace Atlas.Domain.Payroll;

/// <summary>What a payslip line represents. Persisted as text (see PayrollConfigurations).</summary>
public enum PayslipLineKind
{
    /// <summary>A statutory amount withheld from the worker's gross (pension, health, …).</summary>
    EmployeeDeduction,

    /// <summary>Pay withheld for approved unpaid leave (days × daily rate).</summary>
    UnpaidLeave,

    /// <summary>An employer-side statutory cost, shown for cost reporting — NOT withheld from net.</summary>
    EmployerBurden,
}

/// <summary>One priced line of a payslip — a pure calculation result, not an entity. The
/// persisted twin is <see cref="PayslipLine"/>.</summary>
public sealed record PayslipLineItem(string Label, PayslipLineKind Kind, Money Amount);

/// <summary>
/// The full gross-to-net result for one worker for one period: the period gross, every
/// line (employee deductions + unpaid-leave + employer-burden), the net the worker
/// receives, and the total employer cost. Pure output of <see cref="PayrollCalculator"/>
/// — no identity, no persistence, no worker id.
/// </summary>
// 🔍 The invariant a reviewer checks on this type — the payroll cousin of S04's "trace
// one euro": Net + Σ(EmployeeDeduction and UnpaidLeave lines) == Gross, to the minor
// unit, same currency. EmployerBurden lines are NOT part of that identity — they sit on
// top of gross (cost of employment), they are not withheld from it. If that split isn't
// obvious from reading the calculator, the calculator is too clever.
public sealed record PayslipCalculation(
    Money Gross,
    IReadOnlyList<PayslipLineItem> Lines,
    Money Net,
    Money EmployerCost);
