namespace Atlas.Infrastructure.Vendors.PayGrid;

/// <summary>
/// PayGrid — an in-repo mock of the kind of payment vendor you actually get, not the one in the
/// sales deck. It is deliberately awkward so the anti-corruption layer has something real to
/// protect the domain from. This XML doc IS PayGrid's README; treat it as the vendor's manual.
/// </summary>
/// <remarks>
/// <para><b>THE DIALECT (what makes PayGrid PayGrid):</b></para>
/// <list type="bullet">
///   <item>
///     <b>Money is integer cents.</b> <see cref="PayGridChargeRequest.AmountInCents"/> is a
///     <c>long</c> of MINOR units. €40.20 is <c>4020</c>, not <c>40.20m</c>. There is no decimal
///     anywhere in the protocol; converting Atlas's <c>Money</c> to and from cents is the
///     adapter's job, done once, at the boundary.
///   </item>
///   <item>
///     <b>Currency is a bare string.</b> No validation, no type — <c>"EUR"</c>. PayGrid will
///     happily accept <c>"eur"</c> or a typo and fail later; the adapter sends the already-checked
///     <c>CurrencyCode.Value</c>.
///   </item>
///   <item>
///     <b>Statuses are stringly-typed and suffixed.</b> A charge's status is a string from an
///     OPEN, versioned-without-notice vocabulary:
///     <list type="table">
///       <item><term><c>OK_PENDING_&lt;n&gt;</c></term><description>Accepted; settling asynchronously. The trailing number is an internal batch counter — <c>OK_PENDING_2</c> and <c>OK_PENDING_9</c> mean the same thing. Match the FAMILY, never the exact string.</description></item>
///       <item><term><c>SETTLED</c></term><description>Terminal success: funds delivered.</description></item>
///       <item><term><c>REJECTED_&lt;nn&gt;</c></term><description>Declined; the number is a reason code (<c>REJECTED_04</c> = insufficient vendor float, <c>REJECTED_07</c> = blocked beneficiary, …).</description></item>
///       <item><term><c>REJECTED_DUPLICATE</c></term><description>PayGrid believes it has already paid this (its fraud layer flagged a duplicate).</description></item>
///     </list>
///     New statuses appear without a changelog entry. The adapter's policy for a status it has
///     never seen is to FAIL LOUD, never guess (that is the whole point of the ACL).
///   </item>
///   <item>
///     <b>429s happen.</b> A real PayGrid rate-limits unpredictably, returning HTTP 429 as a
///     <see cref="PayGridTransportException"/> BEFORE it ever looks at the charge. The mock makes
///     this a deterministic knob (<see cref="PayGridSettings.RateLimitedChargeCalls"/>) so tests
///     can pin the behaviour instead of flaking on a coin flip.
///   </item>
///   <item>
///     <b>Settlement is asynchronous.</b> CreateCharge returns <c>OK_PENDING_&lt;n&gt;</c> — an
///     acceptance, not a settlement. The money lands (or does not) later; you learn how it went by
///     polling <see cref="GetCharge"/>. There are no webhooks (Atlas gets those in S10).
///   </item>
///   <item>
///     <b>CreateCharge is not idempotent.</b> Every call creates a NEW charge with a NEW id, even
///     for an identical <see cref="PayGridChargeRequest.Reference"/> — the reference is a printed
///     memo, not a dedupe key. PayGrid deduplicates nothing on the way in.
///   </item>
/// </list>
/// <para>
/// The mock is in-process and holds charges in memory. It is not a network client; there is no
/// serialization, no base address. It exists so the adapter's tests can drive PayGrid through its
/// worst day (429s, weird statuses, unmapped statuses) as an executable spec.
/// </para>
/// </remarks>
public sealed class PayGridClient(PayGridSettings settings)
{
    private readonly Dictionary<string, PayGridCharge> _charges = new(StringComparer.Ordinal);
    private int _chargeCalls;
    private int _statusCalls;

    /// <summary>Every charge request PayGrid has received — a spy the adapter's tests read to pin
    /// the wire translation (e.g. Money → integer cents). A mock affordance, not part of a real API.</summary>
    public List<PayGridChargeRequest> ReceivedRequests { get; } = [];

    /// <summary>Create a charge. Returns a charge in <see cref="PayGridSettings.CreatedStatus"/>, or throws a 429.</summary>
    public PayGridCharge CreateCharge(PayGridChargeRequest request)
    {
        // The request reached PayGrid even if the rate limiter then rejects it — record it first.
        ReceivedRequests.Add(request);

        _chargeCalls++;
        if (settings.RateLimitedChargeCalls.Contains(_chargeCalls))
        {
            throw new PayGridTransportException(429, "PayGrid: rate limit exceeded, retry later (429).");
        }

        // A brand-new charge every time — PayGrid deduplicates nothing (see the class remarks).
        var charge = new PayGridCharge(
            $"pg_{Guid.NewGuid():N}", settings.CreatedStatus, request.AmountInCents, request.CurrencyCode);
        _charges[charge.ChargeId] = charge;
        return charge;
    }

    /// <summary>Report a charge's current settlement status, or throw a 429.</summary>
    public PayGridCharge GetCharge(string chargeId)
    {
        _statusCalls++;
        if (settings.RateLimitedStatusCalls.Contains(_statusCalls))
        {
            throw new PayGridTransportException(429, "PayGrid: rate limit exceeded, retry later (429).");
        }

        // Settlement is a separate, later fact than acceptance: the reported status is the
        // configured settlement outcome, not the status the charge was created with.
        long amount = _charges.TryGetValue(chargeId, out PayGridCharge? existing) ? existing.AmountInCents : 0;
        string currency = existing?.CurrencyCode ?? string.Empty;
        return new PayGridCharge(chargeId, settings.SettlementStatus, amount, currency);
    }
}
