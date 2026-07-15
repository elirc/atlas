using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// Golden-file style: hand-computed expected payslips for one DEU and one PHL worker at
/// specific salaries, with the arithmetic shown in comments. When an auditor asks "prove
/// this number", these are the tests that answer — example-based beats property-based when
/// the reader thinks in worked examples (see docs/concepts/money-allocation.md).
/// </summary>
public class PayrollCalculatorGoldenTests
{
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EmploymentRecord Terms(decimal annual, string currency) =>
        EmploymentRecord.Create(WorkerId, new DateOnly(2026, 1, 1), "Engineer", annual, currency, 40, Actor, T0);

    private static Money Line(PayslipCalculation calc, string label) =>
        calc.Lines.Single(l => l.Label == label).Amount;

    [Fact]
    public void Germany_60k_monthly_no_leave()
    {
        // Worker: €60,000/year, paid Monthly. Period = March 2026. No unpaid leave.
        // €60,000 is below every DEU ceiling (62,100 / 90,600), so every base is 60,000.
        //
        //   Monthly gross                = 60,000 / 12                 = 5,000.00
        //   Pension (employee)  9.3%     = (0.093 × 60,000) / 12 = 5,580/12 =   465.00
        //   Health  (employee)  7.3%     = (0.073 × 60,000) / 12 = 4,380/12 =   365.00
        //   Unemploy(employee)  1.3%     = (0.013 × 60,000) / 12 =   780/12 =    65.00
        //   Care    (employee)  1.7%     = (0.017 × 60,000) / 12 = 1,020/12 =    85.00
        //                          withheld total                          =   980.00
        //   Net = 5,000.00 − 980.00                                        = 4,020.00
        //   Employer burden mirrors the employee halves                    =   980.00
        //   Employer cost = 5,000.00 + 980.00                              = 5,980.00
        PayslipCalculation calc = PayrollCalculator.Calculate(new PayrollCalculationInput(
            [Terms(60_000m, "EUR")],
            CountryRulePacks.For(CountryCode.From("DEU")),
            PayPeriod.ForMonth(2026, 3),
            UnpaidLeaveDays: 0));

        Assert.Equal("EUR", calc.Gross.Currency.Value);
        Assert.Equal(5_000.00m, calc.Gross.Amount);

        Assert.Equal(465.00m, Line(calc, "Pension insurance (employee)").Amount);
        Assert.Equal(365.00m, Line(calc, "Health insurance (employee)").Amount);
        Assert.Equal(65.00m, Line(calc, "Unemployment insurance (employee)").Amount);
        Assert.Equal(85.00m, Line(calc, "Long-term care insurance (employee)").Amount);

        Assert.Equal(4_020.00m, calc.Net.Amount);
        Assert.Equal(5_980.00m, calc.EmployerCost.Amount);

        AssertNetPlusWithheldEqualsGross(calc);
    }

    [Fact]
    public void Philippines_600k_monthly_with_two_unpaid_days()
    {
        // Worker: ₱600,000/year, paid Monthly. Period = March 2026. 2 approved unpaid days.
        //
        //   Monthly gross                 = 600,000 / 12                     = 50,000.00
        //   SSS (employee)      4.5% cap 420,000 = (0.045 × 420,000)/12 = 18,900/12 = 1,575.00
        //   PhilHealth (emp.)   2.5%             = (0.025 × 600,000)/12 = 15,000/12 = 1,250.00
        //   Pag-IBIG (employee) 2.0% cap 120,000 = (0.020 × 120,000)/12 =  2,400/12 =   200.00
        //   Unpaid leave: daily = 600,000 / 260 = 2,307.6923… ; × 2 days     =  4,615.38
        //                          withheld total = 3,025.00 + 4,615.38      =  7,640.38
        //   Net = 50,000.00 − 7,640.38                                       = 42,359.62
        //   Employer burden: SSS 9.5%→3,325 + PhilHealth 2.5%→1,250 + Pag-IBIG 2.0%→200 = 4,775.00
        //   Employer cost = 50,000.00 + 4,775.00                             = 54,775.00
        PayslipCalculation calc = PayrollCalculator.Calculate(new PayrollCalculationInput(
            [Terms(600_000m, "PHP")],
            CountryRulePacks.For(CountryCode.From("PHL")),
            PayPeriod.ForMonth(2026, 3),
            UnpaidLeaveDays: 2));

        Assert.Equal("PHP", calc.Gross.Currency.Value);
        Assert.Equal(50_000.00m, calc.Gross.Amount);

        Assert.Equal(1_575.00m, Line(calc, "SSS (employee)").Amount);
        Assert.Equal(1_250.00m, Line(calc, "PhilHealth (employee)").Amount);
        Assert.Equal(200.00m, Line(calc, "Pag-IBIG (employee)").Amount);
        Assert.Equal(4_615.38m, Line(calc, "Unpaid leave").Amount);

        Assert.Equal(42_359.62m, calc.Net.Amount);
        Assert.Equal(54_775.00m, calc.EmployerCost.Amount);

        AssertNetPlusWithheldEqualsGross(calc);
    }

    [Fact]
    public void A_worker_paid_in_the_wrong_currency_for_the_pack_is_refused()
    {
        // A worker with a USD salary reaching the DEU (EUR) pack is a programmer error —
        // the domain refuses rather than pricing nonsense (S04's stance).
        Assert.Throws<DomainRuleException>(() => PayrollCalculator.Calculate(new PayrollCalculationInput(
            [Terms(60_000m, "USD")],
            CountryRulePacks.For(CountryCode.From("DEU")),
            PayPeriod.ForMonth(2026, 3),
            0)));
    }

    // 🔍 The payroll cousin of S04's "trace one euro": every withheld line plus net must
    // reconstitute gross, to the minor unit. Employer burden is NOT part of this identity.
    private static void AssertNetPlusWithheldEqualsGross(PayslipCalculation calc)
    {
        Money withheld = calc.Lines
            .Where(l => l.Kind != PayslipLineKind.EmployerBurden)
            .Aggregate(Money.Zero(calc.Gross.Currency), (acc, l) => acc + l.Amount);

        Assert.Equal(calc.Gross.Amount, (calc.Net + withheld).Amount);
    }
}
