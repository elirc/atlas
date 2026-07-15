namespace Atlas.Domain.Invoicing;

/// <summary>
/// A contractor's bill, walking submit → HR-approve → admin-approve → pay. The aggregate that
/// owns the invoice lines and enforces the two invariants that make it safe to move money
/// from: money leaves ONLY from <see cref="InvoiceStatus.Approved"/> (both approvals in), and a
/// <see cref="InvoiceStatus.Paid"/> invoice is frozen forever. State changes are METHODS with
/// guards, never setters — see <see cref="InvoiceStateMachine"/> and ADR-0010.
/// </summary>
// 🔍 Reviewer's lens for this type: "find who is allowed to move money." The answer is not only
// a role in the use case — it is the STATE. MarkPaid is reachable exactly from Approved or
// Failed, so no caller, however privileged, can pay a Submitted invoice: the second approval is
// a structural precondition of disbursement, not a checkbox the endpoint remembers to check.
public sealed class ContractorInvoice
{
    private readonly List<InvoiceLine> _lines = [];

    public Guid Id { get; }
    public Guid OrgId { get; }
    public Guid ContractorId { get; }

    /// <summary>The single currency the whole invoice is denominated in (the contractor's payout currency).</summary>
    public CurrencyCode Currency { get; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>Who submitted the invoice (v1: an HR manager or admin, on the contractor's behalf).</summary>
    public Guid SubmittedByUserId { get; }

    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    // The two-step approval, remembered as two decision stamps. Read together they are the
    // segregation-of-duties record: who signed off first, who authorised the money.
    public Guid? HrApprovedByUserId { get; private set; }
    public DateTimeOffset? HrApprovedAtUtc { get; private set; }
    public Guid? AdminApprovedByUserId { get; private set; }
    public DateTimeOffset? AdminApprovedAtUtc { get; private set; }

    /// <summary>
    /// The opaque reference the payment provider returned for the disbursement. A plain string
    /// on purpose: the domain records that SOME vendor gave us a handle, never which vendor or
    /// what its codes mean. Translation of vendor dialect stops at the anti-corruption layer
    /// (ADR-0010); by the time a reference reaches this aggregate it is just an id.
    /// </summary>
    public string? VendorReference { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }

    /// <summary>Why the last pay attempt failed, in the provider-neutral words the ACL chose.</summary>
    public string? FailureReason { get; private set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; private set; }

    /// <summary>The billed lines (owned; loaded with the aggregate).</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    private ContractorInvoice(
        Guid id,
        Guid orgId,
        Guid contractorId,
        CurrencyCode currency,
        Guid submittedByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        ContractorId = contractorId;
        Currency = currency;
        SubmittedByUserId = submittedByUserId;
        Status = InvoiceStatus.Draft;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Open a new invoice in <see cref="InvoiceStatus.Draft"/> with its lines. Rejects an empty
    /// invoice and any line whose currency differs from the invoice's — a mixed-currency total
    /// has no meaning without a rate, and the type refuses to invent one (same stance as Money).
    /// </summary>
    public static ContractorInvoice Open(
        Guid orgId,
        Guid contractorId,
        CurrencyCode currency,
        IReadOnlyList<InvoiceLine> lines,
        Guid submittedByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("An invoice must belong to an org.");
        }

        if (contractorId == Guid.Empty)
        {
            throw new DomainRuleException("An invoice must reference a contractor.");
        }

        if (lines is null || lines.Count == 0)
        {
            throw new DomainRuleException("An invoice needs at least one line.");
        }

        // WHY: single-currency is an INVARIANT, checked at construction against the declared
        // currency, not a hope. A line in the wrong currency is a 422 the moment the invoice is
        // built — never a surprise at Total() time or, worse, at pay time.
        foreach (InvoiceLine line in lines)
        {
            if (line.Amount.Currency != currency)
            {
                throw new DomainRuleException(
                    $"Every line must be in the invoice currency ({currency}); " +
                    $"found a line in {line.Amount.Currency}.");
            }
        }

        var invoice = new ContractorInvoice(
            Guid.NewGuid(), orgId, contractorId, currency, submittedByUserId, createdAtUtc);
        invoice._lines.AddRange(lines);
        return invoice;
    }

    /// <summary>
    /// The invoice total — a MEMORY fold over the lines, never a SQL SUM. A method, not a
    /// property, for the same reasons PayrollRun.Total*() are: a computed property would trip
    /// ModelMappingTests and would tempt a caller into folding an un-loaded collection. It is
    /// never null — Open guarantees at least one line.
    /// </summary>
    // GOTCHA: decimal is stored as TEXT on SQLite and cannot be SUM'd in SQL; the currency
    // check in Open means every `+` here is closed (Money refuses to add across currencies).
    public Money Total() => _lines.Select(l => l.Amount).Aggregate((left, right) => left + right);

    /// <summary>Draft → Submitted.</summary>
    public void Submit(DateTimeOffset now)
    {
        Status = InvoiceStateMachine.Next(Status, InvoiceTransition.Submit);
        SubmittedAtUtc = now;
    }

    /// <summary>Submitted → HrApproved. The first of two sign-offs.</summary>
    public void HrApprove(Guid approverUserId, DateTimeOffset now)
    {
        Status = InvoiceStateMachine.Next(Status, InvoiceTransition.HrApprove);
        HrApprovedByUserId = approverUserId;
        HrApprovedAtUtc = now;
    }

    /// <summary>HrApproved → Approved. The second sign-off; after this, money may move.</summary>
    public void AdminApprove(Guid approverUserId, DateTimeOffset now)
    {
        Status = InvoiceStateMachine.Next(Status, InvoiceTransition.AdminApprove);
        AdminApprovedByUserId = approverUserId;
        AdminApprovedAtUtc = now;
    }

    /// <summary>
    /// Approved/Failed → Paid. Records the vendor's reference and freezes the invoice. The
    /// state machine makes "pay a Draft" and "pay an already-Paid invoice" the SAME kind of
    /// impossible — an absent transition key, a precise 422 — so this method carries no
    /// hand-written `if`.
    /// </summary>
    public void MarkPaid(string vendorReference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(vendorReference))
        {
            throw new DomainRuleException("A paid invoice must record the provider's payment reference.");
        }

        Status = InvoiceStateMachine.Next(Status, InvoiceTransition.Pay);
        VendorReference = vendorReference;
        PaidAtUtc = now;
        LastAttemptedAtUtc = now;
        FailureReason = null;
    }

    /// <summary>Approved/Failed → Failed. Records the provider-neutral reason; stays payable.</summary>
    public void MarkPaymentFailed(string reason, DateTimeOffset now)
    {
        Status = InvoiceStateMachine.Next(Status, InvoiceTransition.FailPayment);
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Payment failed." : reason.Trim();
        LastAttemptedAtUtc = now;
    }

    // WHY: EF Core materialization only. _lines is initialised at its field.
    private ContractorInvoice()
    {
    }
}
