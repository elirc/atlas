namespace Atlas.Domain.Invoicing;

/// <summary>Where a contractor invoice is in its life. Persisted as text (see InvoicingConfigurations).</summary>
public enum InvoiceStatus
{
    /// <summary>Created, lines captured, not yet routed for approval.</summary>
    Draft = 1,

    /// <summary>Submitted (on behalf of the contractor for v1); waiting on the first approval.</summary>
    Submitted = 2,

    /// <summary>An HR manager approved the amounts; still needs the admin sign-off before money moves.</summary>
    HrApproved = 3,

    /// <summary>Both approvals in — the invoice is payable.</summary>
    Approved = 4,

    /// <summary>Terminal. The payment provider accepted the disbursement. Immutable.</summary>
    Paid = 5,

    /// <summary>The provider declined the disbursement. Payable again once the cause is fixed.</summary>
    Failed = 6,
}

/// <summary>A named move between two <see cref="InvoiceStatus"/>es.</summary>
public enum InvoiceTransition
{
    Submit,
    HrApprove,
    AdminApprove,
    Pay,
    FailPayment,
}

/// <summary>
/// The invoice's legal moves, as DATA. The guard methods on <see cref="ContractorInvoice"/>
/// consult this table; the exhaustive matrix test consults the same table — so the code and
/// its test can never disagree about what "legal" means.
/// </summary>
// 📘 The S05/S07 lesson a third time: a state machine is a transition TABLE plus guard
// methods, not a status column sprinkled with `if (status == ...)`. Two things worth reading
// OUT of this table:
//   1. Approval is TWO steps, and their ORDER is enforced structurally — HrApprove is only a
//      key from Submitted, AdminApprove only from HrApproved. "Admin-approve before HR" is not
//      a forbidden branch someone remembered to write; it is an absent key.
//   2. Paid has NO outgoing move. That single fact is the "immutable once paid" invariant —
//      MarkPaid/MarkPaymentFailed from Paid throw because Next() finds no key, not because a
//      method checked `if (Status == Paid)`. Failed, by contrast, CAN be paid again (a retry
//      after the cause is fixed), so it keeps its Pay/FailPayment keys.
public static class InvoiceStateMachine
{
    // from-state × transition → to-state. Every LEGAL move is a key; every move NOT present is
    // illegal by definition, which is why the illegal half of the matrix needs no enumeration.
    private static readonly IReadOnlyDictionary<(InvoiceStatus From, InvoiceTransition Move), InvoiceStatus> Table =
        new Dictionary<(InvoiceStatus, InvoiceTransition), InvoiceStatus>
        {
            [(InvoiceStatus.Draft, InvoiceTransition.Submit)] = InvoiceStatus.Submitted,
            [(InvoiceStatus.Submitted, InvoiceTransition.HrApprove)] = InvoiceStatus.HrApproved,
            [(InvoiceStatus.HrApproved, InvoiceTransition.AdminApprove)] = InvoiceStatus.Approved,
            [(InvoiceStatus.Approved, InvoiceTransition.Pay)] = InvoiceStatus.Paid,
            [(InvoiceStatus.Approved, InvoiceTransition.FailPayment)] = InvoiceStatus.Failed,
            // WHY the retry keys: a payment FAILURE is not a dead end. A declined or
            // unreachable-vendor attempt lands in Failed, from which a corrected attempt can be
            // paid. The audit log preserves that a failure happened; the state models "try
            // again", not "give up".
            [(InvoiceStatus.Failed, InvoiceTransition.Pay)] = InvoiceStatus.Paid,
            [(InvoiceStatus.Failed, InvoiceTransition.FailPayment)] = InvoiceStatus.Failed,
        };

    /// <summary>True if <paramref name="move"/> is legal from <paramref name="from"/>.</summary>
    public static bool TryNext(InvoiceStatus from, InvoiceTransition move, out InvoiceStatus to) =>
        Table.TryGetValue((from, move), out to);

    /// <summary>The state after <paramref name="move"/>, or a DomainRuleException (422) if illegal.</summary>
    public static InvoiceStatus Next(InvoiceStatus from, InvoiceTransition move) =>
        TryNext(from, move, out InvoiceStatus to)
            ? to
            : throw new DomainRuleException($"An invoice in {from} cannot {move}.");

    /// <summary>Every legal (from, move, to) triple — the test enumerates against this.</summary>
    public static IReadOnlyCollection<(InvoiceStatus From, InvoiceTransition Move, InvoiceStatus To)> LegalMoves =>
        [.. Table.Select(kv => (kv.Key.From, kv.Key.Move, kv.Value))];
}
