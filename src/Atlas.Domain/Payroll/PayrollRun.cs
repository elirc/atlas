namespace Atlas.Domain.Payroll;

/// <summary>
/// One occurrence of a pay cycle for a specific period: the aggregate that owns the
/// payslips and walks the review → approve → execute workflow. Its natural key is
/// (PayCycleId, PeriodStart) — the identity that makes "prepare this period" idempotent.
/// </summary>
// 🔍 Reviewer's lens for this type: "find who is allowed to call Execute." The answer is
// not only a role — it is the STATE. Execute() is reachable exactly from Approved, so no
// caller, however privileged, can execute a Draft. Authorization here is the state guard
// as much as the role check in the use case. (See docs/adr/ADR-0008.)
public sealed class PayrollRun
{
    private readonly List<Payslip> _payslips = [];

    public Guid Id { get; }
    public Guid OrgId { get; }
    public Guid PayCycleId { get; }

    /// <summary>Natural-key anchor. (PayCycleId, PeriodStart) is unique — see PayrollConfigurations.</summary>
    public DateOnly PeriodStart { get; }
    public DateOnly PeriodEnd { get; }

    public PayrollRunState State { get; private set; }

    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When the run was executed-to-records. Null until Execute; set once, never cleared.</summary>
    public DateTimeOffset? ExecutedAtUtc { get; private set; }

    public IReadOnlyList<Payslip> Payslips => _payslips;

    private PayrollRun(
        Guid id,
        Guid orgId,
        Guid payCycleId,
        PayPeriod period,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        PayCycleId = payCycleId;
        PeriodStart = period.Start;
        PeriodEnd = period.End;
        State = PayrollRunState.Draft;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Open a new run in <see cref="PayrollRunState.Draft"/>, with no payslips yet.</summary>
    public static PayrollRun Create(
        Guid orgId,
        Guid payCycleId,
        PayPeriod period,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("A payroll run must belong to an org.");
        }

        if (payCycleId == Guid.Empty)
        {
            throw new DomainRuleException("A payroll run must belong to a pay cycle.");
        }

        return new PayrollRun(Guid.NewGuid(), orgId, payCycleId, period, createdByUserId, createdAtUtc);
    }

    /// <summary>Attach a computed payslip. Only while Draft — an executed run is frozen.</summary>
    // WHY: payslips are computed once, at prepare time, and never edited (see Payslip).
    // Guarding AddPayslip on Draft means the collection cannot grow after review starts —
    // the numbers a reviewer approved are the numbers that execute.
    public void AddPayslip(Payslip payslip)
    {
        if (State != PayrollRunState.Draft)
        {
            throw new DomainRuleException(
                $"Payslips can only be added while the run is Draft (was {State}).");
        }

        if (payslip.RunId != Id)
        {
            throw new DomainRuleException("That payslip belongs to a different run.");
        }

        _payslips.Add(payslip);
    }

    public void SubmitForReview() => State = PayrollRunStateMachine.Next(State, PayrollTransition.SubmitForReview);

    public void Approve() => State = PayrollRunStateMachine.Next(State, PayrollTransition.Approve);

    public void Reject() => State = PayrollRunStateMachine.Next(State, PayrollTransition.Reject);

    /// <summary>
    /// Move Approved → Executed and stamp the execution time. "Executed" means the run is
    /// RECORDED as final; it does NOT move money — a payment provider does that in S09.
    /// </summary>
    // WHY execute-to-records now, disburse later: separating "the payroll is final and
    // auditable" from "the bank was told to pay" lets each be idempotent on its own terms.
    // This sprint nails down the first (natural key + terminal state); S09 adds the second
    // (the payment provider ACL). Executing here writes the fact, not the transfer.
    public void Execute(DateTimeOffset executedAtUtc)
    {
        State = PayrollRunStateMachine.Next(State, PayrollTransition.Execute);
        ExecutedAtUtc = executedAtUtc;
    }

    // ── Totals: computed in the DOMAIN, folded over payslips, never SUM()'d in SQL ──────
    // GOTCHA: on SQLite (the test provider) decimal is stored as TEXT and cannot be
    // SUM'd or ORDER BY'd in SQL. So run totals are a MEMORY fold over the loaded
    // payslips, not a database aggregate. These are methods, not properties, on purpose:
    // a computed property would trip ModelMappingTests (EF would try to map it or we'd
    // have to declare it unmapped) and would tempt a caller into folding an un-Included
    // collection. A method says "you must have loaded the payslips to ask this."
    // Returns null when the run has no payslips — a run with no workers has no currency to
    // total in, and inventing one (Zero of which currency?) would be a lie.

    public Money? TotalGross() => FoldPayslips(p => p.Gross);

    public Money? TotalNet() => FoldPayslips(p => p.Net);

    public Money? TotalEmployerCost() => FoldPayslips(p => p.EmployerCost);

    private Money? FoldPayslips(Func<Payslip, Money> selector) =>
        _payslips.Count == 0 ? null : _payslips.Select(selector).Aggregate((a, b) => a + b);

    // WHY: EF Core materialization only. _payslips is initialised at its field; State is a
    // value type EF sets via the property's private setter.
    private PayrollRun()
    {
    }
}
