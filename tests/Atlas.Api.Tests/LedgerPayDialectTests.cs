using Atlas.Application.Payroll;
using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;
using Atlas.Infrastructure.Vendors.LedgerPay;

namespace Atlas.Api.Tests;

/// <summary>
/// The LedgerPay anti-corruption layer, tested against its DIALECT — bundled lines, minor-units wire
/// values, half-away-from-zero rounding, an unsupported country, and a surprise line code. These
/// tests ARE the executable spec of LedgerPay's dialect (ADR-0014, the S9 ACL pattern applied a
/// second time): read them to learn what each vendor code means to Atlas, and change them (and only
/// the adapter) when LedgerPay's vocabulary drifts. They also pin the DIVERGENCES from the native
/// engine that shadow mode exists to catch.
/// </summary>
public class LedgerPayDialectTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LedgerPayCalculator Adapter(LedgerPaySettings settings) =>
        new(new LedgerPayClient(settings));

    private static PayrollCalculationInput Input(
        decimal annual, string country, string currency, int month, int unpaid = 0)
    {
        EmploymentRecord terms = EmploymentRecord.Create(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), "Engineer", annual, currency, 40, Actor, T0);
        return new PayrollCalculationInput(
            [terms], CountryRulePacks.For(CountryCode.From(country)), PayPeriod.ForMonth(2026, month), unpaid);
    }

    // ── Granularity + the net identity ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LedgerPay_bundles_deductions_into_one_line_of_each_kind()
    {
        PayslipCalculation calc = await Adapter(LedgerPaySettings.Default)
            .CalculateAsync(Input(60_000m, "DEU", "EUR", month: 3), CancellationToken.None);

        // The whole "different line granularity" the stakeholder warned about: ONE bundled employee
        // line and ONE bundled employer line, where the native engine itemises four each.
        Assert.Single(calc.Lines, l => l.Kind == PayslipLineKind.EmployeeDeduction);
        Assert.Single(calc.Lines, l => l.Kind == PayslipLineKind.EmployerBurden);
        Assert.Contains(calc.Lines, l => l.Label == "Statutory deductions (LedgerPay)");
    }

    [Fact]
    public async Task LedgerPay_output_satisfies_the_net_plus_withheld_equals_gross_identity()
    {
        PayslipCalculation calc = await Adapter(LedgerPaySettings.Default)
            .CalculateAsync(Input(600_000m, "PHL", "PHP", month: 3, unpaid: 2), CancellationToken.None);

        Money withheld = calc.Lines
            .Where(l => l.Kind != PayslipLineKind.EmployerBurden)
            .Aggregate(Money.Zero(calc.Gross.Currency), (acc, l) => acc + l.Amount);

        Assert.Equal(calc.Gross.Amount, (calc.Net + withheld).Amount);
    }

    [Fact]
    public async Task Unpaid_leave_becomes_its_own_line()
    {
        PayslipCalculation calc = await Adapter(LedgerPaySettings.Default)
            .CalculateAsync(Input(600_000m, "PHL", "PHP", month: 3, unpaid: 2), CancellationToken.None);

        // 600,000 / 260 = 2,307.6923… × 2 = 4,615.3846 → half-away-from-zero → 4,615.38.
        PayslipLineItem unpaid = Assert.Single(calc.Lines, l => l.Kind == PayslipLineKind.UnpaidLeave);
        Assert.Equal(4_615.38m, unpaid.Amount.Amount);
    }

    // ── Agreement below caps, DIVERGENCE above them (why shadow mode exists) ──────────────────────

    [Fact]
    public async Task Below_every_cap_LedgerPay_agrees_with_the_native_engine_to_the_cent()
    {
        PayrollCalculationInput input = Input(60_000m, "DEU", "EUR", month: 3);

        PayslipCalculation native = PayrollCalculator.Calculate(input);
        PayslipCalculation ledger = await Adapter(LedgerPaySettings.Default).CalculateAsync(input, CancellationToken.None);

        // €60k is below every DEU ceiling, so LedgerPay's flat 19.6% and our capped components land
        // on the same net — the "usually agrees" case shadow mode should NOT flag.
        Assert.Equal(native.Net.Amount, ledger.Net.Amount);
        Assert.Equal(4_020.00m, ledger.Net.Amount);
        Assert.Equal(native.EmployerCost.Amount, ledger.EmployerCost.Amount);
    }

    [Fact]
    public async Task Above_a_cap_LedgerPay_over_withholds_and_diverges()
    {
        // €120k is above the DEU pension/health ceilings. LedgerPay ignores caps (flat rate), so it
        // withholds MORE than the native engine — the exact model mismatch shadow running catches.
        PayrollCalculationInput input = Input(120_000m, "DEU", "EUR", month: 3);

        PayslipCalculation native = PayrollCalculator.Calculate(input);
        PayslipCalculation ledger = await Adapter(LedgerPaySettings.Default).CalculateAsync(input, CancellationToken.None);

        Assert.True(ledger.Net.Amount < native.Net.Amount, "LedgerPay should over-withhold above caps.");
        Assert.NotEqual(native.Net.Amount, ledger.Net.Amount);
    }

    // ── Wire translation (the spy) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Money_is_translated_to_integer_minor_units_for_ledgerpay()
    {
        var client = new LedgerPayClient(LedgerPaySettings.Default);
        await new LedgerPayCalculator(client).CalculateAsync(
            Input(60_000m, "DEU", "EUR", month: 3), CancellationToken.None);

        LedgerPayCalcRequest sent = Assert.Single(client.ReceivedRequests);
        Assert.Equal(6_000_000, sent.AnnualGrossMinor); // €60,000.00 → 6,000,000 minor units
        Assert.Equal("EUR", sent.IsoCurrency);
        Assert.Equal("DEU", sent.IsoCountry);
        Assert.Equal(12, sent.PeriodsInYear);
        Assert.Equal(2, sent.PeriodOrdinal); // March = index 2 (zero-based)
    }

    // ── The error asymmetry: business "no" (422) vs unmapped state (500) ──────────────────────────

    [Fact]
    public async Task A_country_ledgerpay_cannot_price_is_a_clean_business_rule_error()
    {
        // Empty rate table → LedgerPay refuses the country (E_COUNTRY_UNSUPPORTED). The ACL turns that
        // business "no" into a 422 DomainRuleException, with the vendor code left in the logs only.
        var settings = new LedgerPaySettings
        {
            CountryRates = new Dictionary<string, (decimal, decimal)>(),
        };

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Adapter(settings).CalculateAsync(Input(60_000m, "DEU", "EUR", month: 3), CancellationToken.None));
    }

    [Fact]
    public async Task An_unmapped_line_code_fails_loud_rather_than_guessing()
    {
        // A code the adapter has never been taught is a hole in our model of LedgerPay, not a
        // deduction — it throws (a loud 500), never a guessed mapping (ADR-0010's doctrine).
        var settings = LedgerPaySettings.Default with { UnknownLineCode = "SURPRISE_LEVY" };

        await Assert.ThrowsAsync<UnmappedVendorLineException>(() =>
            Adapter(settings).CalculateAsync(Input(60_000m, "DEU", "EUR", month: 3), CancellationToken.None));
    }
}
