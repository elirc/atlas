using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// Which terms does a run pay when a worker's salary changes mid-period? In v1: the terms
/// in effect on the PERIOD END. No proration of mid-period changes — a deliberate, recorded
/// choice (ADR-0008), pinned here so the answer is a test, not a surprise.
/// </summary>
// 📘 One of the sprint's exercises asks: "an HR manager edits a worker's salary mid-period —
// what does the CURRENT code pay?" This is that test, written to pin the answer. Proration
// (paying part of the month at the old rate and part at the new) is real payroll work, but
// it is deferred: v1 resolves terms once, as of the last day, and pays the whole period at
// them. See ADR-0008's "rejected alternatives".
public class TermsAsOfPeriodEndTests
{
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EmploymentRecord Terms(decimal annual, int year, int month, int day) =>
        EmploymentRecord.Create(WorkerId, new DateOnly(year, month, day), "Engineer", annual, "EUR", 40, Actor, T0);

    [Fact]
    public void A_mid_period_raise_pays_the_period_end_salary()
    {
        // Hired at 60,000 from Jan 1; raised to 120,000 effective March 15.
        // Period = March 2026 (ends March 31). Period-END terms (120,000) govern.
        var hire = Terms(60_000m, 2026, 1, 1);
        var raise = Terms(120_000m, 2026, 3, 15);

        PayslipCalculation calc = PayrollCalculator.Calculate(new PayrollCalculationInput(
            [hire, raise],
            CountryRulePacks.For(CountryCode.From("DEU")),
            PayPeriod.ForMonth(2026, 3),
            0));

        // 120,000 / 12 = 10,000.00 — the raised salary, in full, with NO proration back to
        // the 60,000 the worker was on for the first half of March.
        Assert.Equal(10_000.00m, calc.Gross.Amount);
    }

    [Fact]
    public void A_raise_effective_after_the_period_end_does_not_apply_yet()
    {
        // Same hire, but the raise is effective April 1 — the day AFTER March closes.
        var hire = Terms(60_000m, 2026, 1, 1);
        var raise = Terms(120_000m, 2026, 4, 1);

        PayslipCalculation calc = PayrollCalculator.Calculate(new PayrollCalculationInput(
            [hire, raise],
            CountryRulePacks.For(CountryCode.From("DEU")),
            PayPeriod.ForMonth(2026, 3),
            0));

        // March closes on the 31st; April's raise is invisible to it. 60,000 / 12 = 5,000.00.
        Assert.Equal(5_000.00m, calc.Gross.Amount);
    }
}
