using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Payroll;

/// <summary>
/// The gross-to-net engine: given a worker's terms history, a country rule pack, a
/// period, and approved unpaid-leave days, produce one payslip's worth of numbers. Pure,
/// deterministic, side-effect-free — the same input always yields the same payslip, which
/// is exactly why a payslip can be frozen the instant it is computed (see
/// <see cref="Payslip"/>).
/// </summary>
// 📘 THE COMPOSITION ROOT OF THE DOMAIN. Everything three sprints built meets in this one
// function:
//   • S03 temporal terms  → EmploymentTerms.Resolve(history, period end) picks the salary
//   • S04 Money + packs    → Money arithmetic + the country rule pack's statutory rates
//   • S06 leave            → approved unpaid-leave days become a deduction line
// It is a static function and not a DI class ON PURPOSE: inputs in, payslip out, no clock,
// no repository, no ambient anything. Its testability IS the feature — a golden-file test
// can pin exact numbers because nothing can leak in to change them (see the debate in
// docs/adr/ADR-0008 and docs/concepts/money-allocation.md).
public static class PayrollCalculator
{
    // WHY 12: v1 is Monthly-only (PayFrequency), so the year has 12 pay periods and annual
    // statutory figures are split 12 ways. When a second cadence lands this constant moves
    // onto the input (it becomes a property of the frequency) — flagged in NOTES/ADR-0008.
    private const int PeriodsPerYear = 12;

    // WHY 260: the daily rate for an unpaid-leave deduction is annual salary ÷ 260 —
    // 52 weeks × 5 working days. This is a CHOSEN convention, not a fact: some payrolls use
    // 261/262 (actual weekdays in the year) or 21.67 days/month. We pick a stable, legible
    // 260 and write it down so the deduction is reproducible; day-count conventions are
    // exactly the kind of "policy must be chosen and tested" the course keeps returning to.
    private const decimal WorkingDaysPerYear = 260m;

    public static PayslipCalculation Calculate(PayrollCalculationInput input)
    {
        if (input.UnpaidLeaveDays < 0)
        {
            throw new DomainRuleException("Unpaid leave days cannot be negative.");
        }

        // S03 lands here: which terms governed on the LAST day of the period? Resolved in
        // memory from the full history — no stored "current salary", no extra query.
        EmploymentRecord terms = EmploymentTerms.Resolve(input.TermsHistory, input.Period.End)
            ?? throw new DomainRuleException(
                $"No employment terms are in effect on {input.Period.End:yyyy-MM-dd}.");

        var currency = CurrencyCode.From(terms.SalaryCurrency);

        // The pack prices this country in its own currency; the worker is paid in it too.
        // A mismatch is a programmer error (a DEU worker with a USD salary reaching a DEU
        // pack), but the domain refuses it rather than pricing nonsense — S04's stance.
        if (currency != input.Pack.Currency)
        {
            throw new DomainRuleException(
                $"Worker is paid in {currency} but {input.Pack.Country} payroll is in {input.Pack.Currency}.");
        }

        decimal annualAmount = terms.AnnualSalary;
        int index = input.Period.IndexInYear;

        // Period gross = this period's slice of annual, allocated so the 12 slices sum
        // EXACTLY to annual over the year (largest-remainder). See PeriodShareOf.
        Money periodGross = PeriodShareOf(Money.Of(annualAmount, currency), index);

        var lines = new List<PayslipLineItem>();

        // ── Employee deductions: what comes OUT of gross to reach net ────────────────────
        Money deductionsTotal = Money.Zero(currency);
        foreach (DeductionComponent component in input.Pack.EmployeeDeductions)
        {
            Money periodShare = PeriodShareOf(
                Money.Of(component.Rate * component.AssessableBase(annualAmount), currency),
                index);
            lines.Add(new PayslipLineItem(component.Name, PayslipLineKind.EmployeeDeduction, periodShare));
            deductionsTotal += periodShare;
        }

        // ── Unpaid leave: days × daily rate, this period only (never allocated across the
        // year — the leave happened THIS period) ────────────────────────────────────────
        if (input.UnpaidLeaveDays > 0)
        {
            Money dailyRate = Money.Of(annualAmount / WorkingDaysPerYear, currency);
            Money unpaid = (dailyRate * input.UnpaidLeaveDays).Rounded();
            lines.Add(new PayslipLineItem("Unpaid leave", PayslipLineKind.UnpaidLeave, unpaid));
            deductionsTotal += unpaid;
        }

        // The identity a reviewer checks: net + every withheld line == gross, to the cent.
        Money net = periodGross - deductionsTotal;

        // ── Employer burden: cost ON TOP of gross, shown for reporting, not withheld ─────
        Money employerCost = periodGross;
        foreach (BurdenComponent component in input.Pack.EmployerBurden)
        {
            Money periodShare = PeriodShareOf(
                Money.Of(component.Rate * component.AssessableBase(annualAmount), currency),
                index);
            lines.Add(new PayslipLineItem(component.Name, PayslipLineKind.EmployerBurden, periodShare));
            employerCost += periodShare;
        }

        return new PayslipCalculation(periodGross, lines, net, employerCost);
    }

    // Split an annual figure across the 12 monthly periods: round annual / 12 to the cent,
    // the same share every period. Simple and obvious.
    private static Money PeriodShareOf(Money annual, int periodIndex) =>
        Money.Of(annual.Amount / PeriodsPerYear, annual.Currency).Rounded();
}
