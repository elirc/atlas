using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// The penny-allocation property, the GREEN end of this sprint's RED→GREEN arc: the twelve
/// monthly payslips a worker receives across a year must sum, LINE BY LINE, to exactly the
/// annual figures — no cent lost or conjured.
/// </summary>
// 📘 Why this is the test that was RED. The calculator first split each annual figure the
// naive way — Money.Of(annual / 12).Rounded() — which for any salary not divisible by 12
// makes the twelve shares miss the annual by a cent or two (€100,000/12 = 8,333.33…, and
// 8,333.33 × 12 = 99,999.96). This test failed on exactly those salaries. The fix was
// Money.Allocate(12): largest-remainder, so the twelve shares sum to the annual to the
// cent. Deterministic adversarial salaries stand in for a CsCheck property (no new package
// — see NOTES.md); each divisor-hostile amount is a witness the naive version could not
// pass. Delete Money.Allocate and re-run: watch these go red again.
public class PayrollAllocationTests
{
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EmploymentRecord Terms(decimal annual, string currency) =>
        EmploymentRecord.Create(WorkerId, new DateOnly(2026, 1, 1), "Engineer", annual, currency, 40, Actor, T0);

    [Theory]
    // Salaries chosen to be hostile to /12: none divides into whole cents.
    [InlineData("DEU", "EUR", 100_000)]   // 100,000 / 12 = 8,333.33… → naive loses 4c/yr
    [InlineData("DEU", "EUR", 50_000)]    // 50,000 / 12  = 4,166.66… → naive gains 4c/yr
    [InlineData("DEU", "EUR", 99_999)]
    [InlineData("PHL", "PHP", 600_001)]
    [InlineData("USA", "USD", 123_457)]
    public void Twelve_periods_sum_exactly_to_the_annual_figures(
        string country, string currency, int annualSalary)
    {
        CurrencyCode ccy = CurrencyCode.From(currency);
        CountryRulePack pack = CountryRulePacks.For(CountryCode.From(country));
        EmploymentRecord record = Terms(annualSalary, currency);

        Money grossTotal = Money.Zero(ccy);
        var lineTotals = new Dictionary<string, Money>();

        for (int month = 1; month <= 12; month++)
        {
            PayslipCalculation calc = PayrollCalculator.Calculate(new PayrollCalculationInput(
                [record], pack, PayPeriod.ForMonth(2026, month), UnpaidLeaveDays: 0));

            grossTotal += calc.Gross;
            foreach (PayslipLineItem line in calc.Lines)
            {
                Money running = lineTotals.GetValueOrDefault(line.Label) ?? Money.Zero(ccy);
                lineTotals[line.Label] = running + line.Amount;
            }
        }

        // Gross: the twelve monthly slices reconstitute the annual salary to the cent.
        Assert.Equal((decimal)annualSalary, grossTotal.Amount);

        // Each statutory line — employee and employer — reconstitutes its rounded annual.
        foreach (DeductionComponent component in pack.EmployeeDeductions)
        {
            Assert.Equal(RoundedAnnual(component.Rate, component.AssessableBase(annualSalary)),
                lineTotals[component.Name].Amount);
        }

        foreach (BurdenComponent component in pack.EmployerBurden)
        {
            Assert.Equal(RoundedAnnual(component.Rate, component.AssessableBase(annualSalary)),
                lineTotals[component.Name].Amount);
        }
    }

    private static decimal RoundedAnnual(decimal rate, decimal assessableBase) =>
        decimal.Round(rate * assessableBase, 2, MidpointRounding.ToEven);
}
