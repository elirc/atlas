namespace Atlas.Domain.Payroll;

/// <summary>
/// One worker's frozen pay statement for one period: gross, the lines that take it to
/// net, the net itself, and the employer cost — all as <see cref="Money"/>. A payslip is
/// computed ONCE, when the run is prepared, and never edited.
/// </summary>
// ⚠️ IMMUTABILITY IS THE POINT (accounting 101). This entity has no mutators — not one.
// A payslip is a document, and documents are never edited after the fact; a correction is
// a NEW document that supersedes the old, so the history of what was paid, and what was
// believed, both survive. That is why there is no `Recompute()`, no setter, no
// `Adjust()`. If the numbers were wrong, the run that produced them is wrong, and the fix
// is a new run — not a quiet overwrite that erases the evidence. (S15 owns the
// corrections runbook; this sprint just refuses to make corrections easy-and-wrong.)
public sealed class Payslip
{
    private readonly List<PayslipLine> _lines = [];

    public Guid Id { get; }
    public Guid RunId { get; }
    public Guid WorkerId { get; }

    /// <summary>Denormalised from the run so a payslip can be read tenant-safely by id alone.</summary>
    public Guid OrgId { get; }

    public DateOnly PeriodStart { get; }
    public DateOnly PeriodEnd { get; }

    public Money Gross { get; }
    public Money Net { get; }
    public Money EmployerCost { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<PayslipLine> Lines => _lines;

    private Payslip(
        Guid id,
        Guid runId,
        Guid workerId,
        Guid orgId,
        DateOnly periodStart,
        DateOnly periodEnd,
        Money gross,
        Money net,
        Money employerCost,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        RunId = runId;
        WorkerId = workerId;
        OrgId = orgId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Gross = gross;
        Net = net;
        EmployerCost = employerCost;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Freeze a pure <see cref="PayslipCalculation"/> into a durable payslip owned by a
    /// run and a worker. The lines are copied out of the transient calculation into owned
    /// <see cref="PayslipLine"/> rows; nothing shares a reference with the calculator's
    /// output.
    /// </summary>
    // LEARN: mirrors S04's Quote.FromBreakdown exactly — the pure engine yields a value
    // with no identity, and the entity snapshots it. Two types (calculation vs payslip)
    // keep persistence concerns out of the engine and keep the engine's output trivially
    // testable.
    public static Payslip FromCalculation(
        Guid runId,
        Guid workerId,
        Guid orgId,
        PayPeriod period,
        PayslipCalculation calculation,
        DateTimeOffset createdAtUtc)
    {
        if (runId == Guid.Empty || workerId == Guid.Empty || orgId == Guid.Empty)
        {
            throw new DomainRuleException("A payslip needs a run, a worker, and an org.");
        }

        var payslip = new Payslip(
            Guid.NewGuid(), runId, workerId, orgId, period.Start, period.End,
            calculation.Gross, calculation.Net, calculation.EmployerCost, createdAtUtc);

        foreach (PayslipLineItem item in calculation.Lines)
        {
            payslip._lines.Add(PayslipLine.FromItem(item));
        }

        return payslip;
    }

    // WHY: EF Core materialization only. Owned Money values and _lines are populated by
    // EF; the public factory remains the only door for new payslips.
    private Payslip()
    {
        Gross = null!;
        Net = null!;
        EmployerCost = null!;
    }
}
