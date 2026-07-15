using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// The vendor resolver — EmploymentTerms.Resolve's twin, so the tests are its twin too: latest
/// effective wins, order of arrival is irrelevant (input is shuffled to prove it), and an empty
/// history resolves to the fallback. Plus the pending-switch lookup the shadow gate hangs on.
/// </summary>
public class VendorSelectionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly CountryCode Deu = CountryCode.From("DEU");
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static VendorAssignment Assign(PayrollVendor vendor, DateOnly effectiveFrom, DateTimeOffset? created = null) =>
        VendorAssignment.Create(Org, Deu, effectiveFrom, vendor, Actor, created ?? T0);

    [Fact]
    public void Empty_history_resolves_to_the_fallback()
    {
        PayrollVendor resolved = VendorSelection.Resolve(
            [], new DateOnly(2026, 6, 1), fallback: PayrollVendor.AtlasNative);

        Assert.Equal(PayrollVendor.AtlasNative, resolved);
    }

    [Fact]
    public void The_latest_assignment_effective_on_or_before_the_date_wins()
    {
        VendorAssignment[] history =
        [
            Assign(PayrollVendor.AtlasNative, new DateOnly(2026, 1, 1)),
            Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1)),
        ];

        // Before the switch: still native. On/after the switch: LedgerPay.
        Assert.Equal(PayrollVendor.AtlasNative, VendorSelection.Resolve(history, new DateOnly(2026, 7, 31), PayrollVendor.AtlasNative));
        Assert.Equal(PayrollVendor.LedgerPay, VendorSelection.Resolve(history, new DateOnly(2026, 8, 1), PayrollVendor.AtlasNative));
        Assert.Equal(PayrollVendor.LedgerPay, VendorSelection.Resolve(history, new DateOnly(2026, 12, 31), PayrollVendor.AtlasNative));
    }

    [Fact]
    public void Resolution_is_independent_of_input_order()
    {
        // Same records, reversed — order of arrival must not change the answer (it is a fold).
        VendorAssignment[] forward =
        [
            Assign(PayrollVendor.AtlasNative, new DateOnly(2026, 1, 1)),
            Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1)),
        ];
        VendorAssignment[] reversed = [forward[1], forward[0]];

        var asOf = new DateOnly(2026, 9, 1);
        Assert.Equal(
            VendorSelection.Resolve(forward, asOf, PayrollVendor.AtlasNative),
            VendorSelection.Resolve(reversed, asOf, PayrollVendor.AtlasNative));
    }

    [Fact]
    public void Same_day_records_break_the_tie_by_latest_created()
    {
        // The DB forbids duplicate (Org, Country, EffectiveFrom), but Resolve must stay total over
        // any in-memory list — latest-created wins so the fold is deterministic.
        var day = new DateOnly(2026, 8, 1);
        VendorAssignment[] history =
        [
            Assign(PayrollVendor.AtlasNative, day, T0),
            Assign(PayrollVendor.LedgerPay, day, T0.AddHours(1)),
        ];

        Assert.Equal(PayrollVendor.LedgerPay, VendorSelection.Resolve(history, day, PayrollVendor.AtlasNative));
    }

    [Fact]
    public void PendingSwitch_returns_the_earliest_future_assignment_or_null()
    {
        VendorAssignment[] history =
        [
            Assign(PayrollVendor.AtlasNative, new DateOnly(2026, 1, 1)),
            Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1)),
        ];

        // Before the flip: the LedgerPay switch is pending. After it: nothing pending (auto-expired).
        VendorAssignment? pendingInJuly = VendorSelection.PendingSwitch(history, new DateOnly(2026, 7, 1));
        Assert.NotNull(pendingInJuly);
        Assert.Equal(PayrollVendor.LedgerPay, pendingInJuly!.Vendor);

        Assert.Null(VendorSelection.PendingSwitch(history, new DateOnly(2026, 8, 1)));
    }
}
