using Atlas.Domain;

namespace Atlas.Application.Payments;

/// <summary>
/// Everything a payment provider needs to disburse ONE invoice, in Atlas's vocabulary: which
/// invoice, how much (a domain <see cref="Money"/>, currency attached), and a human-readable
/// memo for the vendor's statement. The anti-corruption layer translates this into whatever
/// shape the vendor actually wants (PayGrid wants integer cents and a flat currency string).
/// </summary>
// ⚠️ Notice what is NOT here: an idempotency key. Threading a caller-supplied
// Idempotency-Key through money-moving requests is the S10 theme (the middleware and the
// dedupe store arrive together); this port carries only the business facts of the payment.
// Amount stays a Money — the adapter, not the application, is where it becomes cents.
public sealed record PaymentInstruction(Guid InvoiceId, Money Amount, string Memo);
