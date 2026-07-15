using Atlas.Application.Errors;
using Atlas.Application.Payments;
using Atlas.Domain;
using Atlas.Infrastructure.Vendors.PayGrid;

namespace Atlas.Api.Tests;

/// <summary>
/// The anti-corruption layer, tested against PayGrid's WORST day — 429s, suffixed statuses, a
/// rejection, a duplicate, and a status we've never seen. These tests ARE the executable spec of
/// PayGrid's dialect the debate demanded: read them to learn what each vendor string means to
/// Atlas, and change them (and only the adapter) when PayGrid's vocabulary drifts.
/// </summary>
public class PayGridDialectTests
{
    private static PayGridPaymentProvider Adapter(PayGridSettings settings) =>
        new(new PayGridClient(settings));

    private static PaymentInstruction Instruction() =>
        new(Guid.NewGuid(), Money.Of(200m, CurrencyCode.From("USD")), "Dana Devlin");

    // ── Charge status → PaymentResult (the closed set) ─────────────────────────────────────────

    [Theory]
    [InlineData("SETTLED", SettlementStatus.Settled)]
    [InlineData("OK_PENDING_2", SettlementStatus.Pending)]
    // The exercise status: a NEW batch counter on a KNOWN family. Prefix matching means it maps
    // with zero code changes — equality matching would have fail-loud'd on it.
    [InlineData("OK_PENDING_9", SettlementStatus.Pending)]
    public async Task Accepted_statuses_map_to_succeeded(string vendorStatus, SettlementStatus expected)
    {
        PaymentResult result = await Adapter(new PayGridSettings { CreatedStatus = vendorStatus })
            .PayAsync(Instruction(), CancellationToken.None);

        var succeeded = Assert.IsType<PaymentResult.Succeeded>(result);
        Assert.Equal(expected, succeeded.Settlement);
        Assert.False(string.IsNullOrWhiteSpace(succeeded.Reference.Value));
    }

    [Fact]
    public async Task A_rejection_maps_to_payment_rejected_with_neutral_prose_and_the_raw_code()
    {
        PaymentResult result = await Adapter(new PayGridSettings { CreatedStatus = "REJECTED_04" })
            .PayAsync(Instruction(), CancellationToken.None);

        var rejected = Assert.IsType<PaymentResult.PaymentRejected>(result);
        Assert.Equal("REJECTED_04", rejected.VendorCode);
        // The reason the DOMAIN records is provider-neutral prose, not the vendor code.
        Assert.DoesNotContain("REJECTED_04", rejected.Reason);
    }

    [Fact]
    public async Task A_duplicate_maps_to_already_paid()
    {
        PaymentResult result = await Adapter(new PayGridSettings { CreatedStatus = "REJECTED_DUPLICATE" })
            .PayAsync(Instruction(), CancellationToken.None);

        Assert.IsType<PaymentResult.AlreadyPaid>(result);
    }

    [Fact]
    public async Task An_unmapped_status_fails_loud_rather_than_guessing()
    {
        // The whole unknown-state policy: a status we've never taught the adapter is a hole in our
        // model of PayGrid, not a business outcome — so it throws, and becomes a loud 500.
        await Assert.ThrowsAsync<UnmappedVendorStateException>(() =>
            Adapter(new PayGridSettings { CreatedStatus = "WOBBLE_7" })
                .PayAsync(Instruction(), CancellationToken.None));
    }

    [Fact]
    public async Task A_429_on_charge_maps_to_a_typed_vendor_unavailable_result_not_an_exception()
    {
        PaymentResult result = await Adapter(new PayGridSettings { RateLimitedChargeCalls = new HashSet<int> { 1 } })
            .PayAsync(Instruction(), CancellationToken.None);

        // Reported as a typed result the use case can act on — the transport error stops here.
        Assert.IsType<PaymentResult.VendorUnavailable>(result);
    }

    [Fact]
    public async Task Money_is_translated_to_integer_cents_for_paygrid()
    {
        var payGrid = new PayGridClient(new PayGridSettings());
        var adapter = new PayGridPaymentProvider(payGrid);

        await adapter.PayAsync(
            new PaymentInstruction(Guid.NewGuid(), Money.Of(40.20m, CurrencyCode.From("EUR")), "Dana"),
            CancellationToken.None);

        PayGridChargeRequest sent = Assert.Single(payGrid.ReceivedRequests);
        Assert.Equal(4020, sent.AmountInCents);
        Assert.Equal("EUR", sent.CurrencyCode);
    }

    // ── Settlement status → SettlementStatus (reconciliation) ──────────────────────────────────

    [Theory]
    [InlineData("SETTLED", SettlementStatus.Settled)]
    [InlineData("OK_PENDING_2", SettlementStatus.Pending)]
    [InlineData("REJECTED_09", SettlementStatus.Failed)]
    public async Task Settlement_statuses_map_to_the_closed_set(string vendorStatus, SettlementStatus expected)
    {
        PaymentSettlement settlement = await Adapter(new PayGridSettings { SettlementStatus = vendorStatus })
            .GetSettlementAsync(new VendorPaymentReference("pg_ref"), CancellationToken.None);

        Assert.Equal(expected, settlement.Status);
    }

    [Fact]
    public async Task An_unmapped_settlement_status_fails_loud()
    {
        await Assert.ThrowsAsync<UnmappedVendorStateException>(() =>
            Adapter(new PayGridSettings { SettlementStatus = "ZONK_1" })
                .GetSettlementAsync(new VendorPaymentReference("pg_ref"), CancellationToken.None));
    }

    [Fact]
    public async Task A_429_on_settlement_is_a_retryable_unavailable_exception()
    {
        // A read has no closed result set to fold "unavailable" into, so it surfaces as the
        // retryable application exception (which the Api maps to 503).
        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            Adapter(new PayGridSettings { RateLimitedStatusCalls = new HashSet<int> { 1 } })
                .GetSettlementAsync(new VendorPaymentReference("pg_ref"), CancellationToken.None));
    }
}
