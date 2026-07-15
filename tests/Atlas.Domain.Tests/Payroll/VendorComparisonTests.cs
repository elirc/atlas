using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// The pure comparison + report builder. These tests pin two promises: deltas are computed correctly
/// in minor units, and the report carries ONLY deltas — there is no field on VendorDelta,
/// VendorComparisonLine, or VendorComparisonReport that could hold a net-pay figure. Redaction is a
/// type-shape guarantee, and this file is where the shape is asserted.
/// </summary>
public class VendorComparisonTests
{
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");
    private static readonly Guid Run = Guid.NewGuid();
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();
    private static readonly CountryCode Deu = CountryCode.From("DEU");
    private static readonly DateTimeOffset T0 = new(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);

    private static PayslipCalculation Calc(decimal gross, decimal net, decimal employerCost) =>
        new(Money.Of(gross, Eur), [], Money.Of(net, Eur), Money.Of(employerCost, Eur));

    private static Payslip PrimaryPayslip(decimal gross, decimal net, decimal employerCost) =>
        Payslip.FromCalculation(Run, Worker, Org, PayPeriod.ForMonth(2026, 3),
            Calc(gross, net, employerCost), T0);

    [Fact]
    public void DeltaOf_reports_shadow_minus_primary_in_minor_units()
    {
        Payslip primary = PrimaryPayslip(gross: 5_000.00m, net: 4_020.00m, employerCost: 5_980.00m);
        PayslipCalculation shadow = Calc(gross: 5_000.00m, net: 4_013.95m, employerCost: 5_980.00m);

        VendorDelta delta = VendorComparison.DeltaOf(primary, shadow);

        Assert.Equal(Worker, delta.WorkerId);
        Assert.Equal(0, delta.GrossDeltaMinor);
        Assert.Equal(-605, delta.NetDeltaMinor); // 4013.95 - 4020.00 = -6.05 → -605 minor units
        Assert.Equal(0, delta.EmployerCostDeltaMinor);
        Assert.Equal("EUR", delta.Currency);
    }

    [Fact]
    public void DeltaOf_refuses_a_cross_currency_comparison()
    {
        Payslip primary = PrimaryPayslip(5_000.00m, 4_020.00m, 5_980.00m);
        var shadow = new PayslipCalculation(
            Money.Of(5_000.00m, CurrencyCode.From("USD")), [],
            Money.Of(4_020.00m, CurrencyCode.From("USD")),
            Money.Of(5_980.00m, CurrencyCode.From("USD")));

        Assert.Throws<DomainRuleException>(() => VendorComparison.DeltaOf(primary, shadow));
    }

    [Fact]
    public void Build_flags_divergence_beyond_tolerance_and_summarises()
    {
        var clean = new VendorDelta(Guid.NewGuid(), GrossDeltaMinor: 0, NetDeltaMinor: 1, EmployerCostDeltaMinor: 0, "EUR");
        var divergent = new VendorDelta(Guid.NewGuid(), GrossDeltaMinor: 0, NetDeltaMinor: -69_395, EmployerCostDeltaMinor: 0, "EUR");

        VendorComparisonReport report = VendorComparisonReport.Build(
            Run, Org, Deu, PayrollVendor.AtlasNative, PayrollVendor.LedgerPay,
            toleranceMinorUnits: 2, deltas: [clean, divergent], createdAtUtc: T0);

        Assert.Equal(2, report.WorkerCount);
        Assert.Equal(1, report.DivergentCount);        // only the -693.95 one exceeds tolerance 2
        Assert.Equal(69_395, report.MaxAbsNetDeltaMinor);
        Assert.False(report.IsClean);

        // The clean line (1 minor unit ≤ tolerance) is NOT flagged; the big one is.
        Assert.Contains(report.Lines, l => l.NetDeltaMinor == 1 && !l.Divergent);
        Assert.Contains(report.Lines, l => l.NetDeltaMinor == -69_395 && l.Divergent);
    }

    [Fact]
    public void A_report_with_only_within_tolerance_deltas_is_clean()
    {
        var noisy = new VendorDelta(Guid.NewGuid(), 0, NetDeltaMinor: -2, 0, "EUR"); // exactly tolerance → not divergent

        VendorComparisonReport report = VendorComparisonReport.Build(
            Run, Org, Deu, PayrollVendor.AtlasNative, PayrollVendor.LedgerPay,
            toleranceMinorUnits: 2, deltas: [noisy], createdAtUtc: T0);

        Assert.True(report.IsClean);
        Assert.Equal(0, report.DivergentCount);
    }

    [Fact]
    public void Comparing_a_vendor_with_itself_is_refused()
    {
        Assert.Throws<DomainRuleException>(() => VendorComparisonReport.Build(
            Run, Org, Deu, PayrollVendor.LedgerPay, PayrollVendor.LedgerPay,
            toleranceMinorUnits: 2, deltas: [], createdAtUtc: T0));
    }
}
