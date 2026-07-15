using Atlas.Domain.Countries;

namespace Atlas.Domain.Payroll;

/// <summary>
/// One worker's persisted comparison line — deltas only, plus the frozen verdict of whether
/// this worker's net diverged beyond tolerance. Owned by a <see cref="VendorComparisonReport"/>,
/// immutable, a snapshot at creation, because a comparison never recomputes (the run it
/// describes is frozen the instant it is prepared).
/// </summary>
public sealed class VendorComparisonLine
{
    public Guid Id { get; }
    public Guid WorkerId { get; }

    public long GrossDeltaMinor { get; }
    public long NetDeltaMinor { get; }
    public long EmployerCostDeltaMinor { get; }

    /// <summary>Currency of the deltas, for display context (deltas are in this currency's minor units).</summary>
    public string Currency { get; }

    /// <summary>
    /// Frozen at build time: was |NetDeltaMinor| strictly greater than the report's tolerance?
    /// Stored as a fact, not recomputed — the tolerance that judged it lives on the report and
    /// could change later; the verdict a human acted on must not silently move under them.
    /// </summary>
    public bool Divergent { get; }

    private VendorComparisonLine(
        Guid id,
        Guid workerId,
        long grossDeltaMinor,
        long netDeltaMinor,
        long employerCostDeltaMinor,
        string currency,
        bool divergent)
    {
        Id = id;
        WorkerId = workerId;
        GrossDeltaMinor = grossDeltaMinor;
        NetDeltaMinor = netDeltaMinor;
        EmployerCostDeltaMinor = employerCostDeltaMinor;
        Currency = currency;
        Divergent = divergent;
    }

    internal static VendorComparisonLine From(VendorDelta delta, int toleranceMinorUnits) =>
        new(Guid.NewGuid(), delta.WorkerId, delta.GrossDeltaMinor, delta.NetDeltaMinor,
            delta.EmployerCostDeltaMinor, delta.Currency,
            Math.Abs(delta.NetDeltaMinor) > toleranceMinorUnits);

    // WHY: EF Core materialization only.
    private VendorComparisonLine()
    {
        Currency = null!;
    }
}

/// <summary>
/// A frozen "run both vendors, then compare" report for one payroll run: which two vendors,
/// what tolerance judged them, and one delta line per worker — with the sensitive absolutes
/// already redacted to deltas (see <see cref="VendorComparison"/>). Persisted so finance can
/// review divergences BEFORE an org flips vendor, and never edited afterward.
/// </summary>
// 📘 SHADOW RUNNING, THE ENTITY. This is how a real payroll migration de-risks: you do NOT
// flip a client to a new vendor and hope; you run both engines over the same period, store
// the differences, and let a human read them before anyone's take-home pay depends on the new
// maths. The report is a DOCUMENT (like a payslip): computed once, immutable, superseded by a
// newer report rather than mutated. Its summary (WorkerCount / DivergentCount / the worst net
// delta) is stored, not computed on read — the run is frozen, so the summary is a fact of that
// moment, and storing it keeps the "how bad is it?" query off a decimal fold. Gating and
// time-boxing (which orgs may generate one, and until when) live in the application policy,
// not here — the domain models the artifact; the policy decides who is allowed to make one.
public sealed class VendorComparisonReport
{
    private readonly List<VendorComparisonLine> _lines = [];

    public Guid Id { get; }
    public Guid OrgId { get; }

    /// <summary>The native/primary run this report shadows. One report supersedes another for the same run.</summary>
    public Guid RunId { get; }

    public CountryCode Country { get; }

    /// <summary>The run's pinned vendor — the baseline the shadow is measured against.</summary>
    public PayrollVendor PrimaryVendor { get; }

    /// <summary>The vendor being trialled (the pending switch's vendor).</summary>
    public PayrollVendor ShadowVendor { get; }

    /// <summary>Net deltas at or below this (in minor units) are treated as rounding-dialect noise, not divergence.</summary>
    public int ToleranceMinorUnits { get; }

    public int WorkerCount { get; }
    public int DivergentCount { get; }

    /// <summary>The largest |net delta| across all workers, in minor units — the report's headline severity.</summary>
    public long MaxAbsNetDeltaMinor { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<VendorComparisonLine> Lines => _lines;

    /// <summary>True when no worker's net diverged beyond tolerance — safe to flip on these numbers.</summary>
    // NOTE: a computed convenience (never persisted). Declared in ModelMappingTests'
    // DeliberatelyUnmapped list, exactly where a reviewer sees every such exception.
    public bool IsClean => DivergentCount == 0;

    private VendorComparisonReport(
        Guid id,
        Guid orgId,
        Guid runId,
        CountryCode country,
        PayrollVendor primaryVendor,
        PayrollVendor shadowVendor,
        int toleranceMinorUnits,
        int workerCount,
        int divergentCount,
        long maxAbsNetDeltaMinor,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        RunId = runId;
        Country = country;
        PrimaryVendor = primaryVendor;
        ShadowVendor = shadowVendor;
        ToleranceMinorUnits = toleranceMinorUnits;
        WorkerCount = workerCount;
        DivergentCount = divergentCount;
        MaxAbsNetDeltaMinor = maxAbsNetDeltaMinor;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Build a report from per-worker deltas (already redacted by <see cref="VendorComparison"/>)
    /// and the tolerance that judges them. The summary counters are folded here, once, from the
    /// deltas — so the stored WorkerCount/DivergentCount/MaxAbsNetDeltaMinor can never disagree
    /// with the lines they summarise.
    /// </summary>
    public static VendorComparisonReport Build(
        Guid runId,
        Guid orgId,
        CountryCode country,
        PayrollVendor primaryVendor,
        PayrollVendor shadowVendor,
        int toleranceMinorUnits,
        IReadOnlyList<VendorDelta> deltas,
        DateTimeOffset createdAtUtc)
    {
        if (runId == Guid.Empty || orgId == Guid.Empty)
        {
            throw new DomainRuleException("A comparison report needs a run and an org.");
        }

        if (toleranceMinorUnits < 0)
        {
            throw new DomainRuleException("Divergence tolerance cannot be negative.");
        }

        if (primaryVendor == shadowVendor)
        {
            // Comparing a vendor with itself is a no-op that would always be "clean" and mislead
            // — the caller (ShadowComparePayrollRun) guards this too, defence in depth.
            throw new DomainRuleException("The shadow vendor must differ from the run's vendor.");
        }

        var report = new VendorComparisonReport(
            Guid.NewGuid(), orgId, runId, country, primaryVendor, shadowVendor,
            toleranceMinorUnits,
            workerCount: deltas.Count,
            divergentCount: deltas.Count(d => Math.Abs(d.NetDeltaMinor) > toleranceMinorUnits),
            maxAbsNetDeltaMinor: deltas.Count == 0 ? 0 : deltas.Max(d => Math.Abs(d.NetDeltaMinor)),
            createdAtUtc);

        foreach (VendorDelta delta in deltas)
        {
            report._lines.Add(VendorComparisonLine.From(delta, toleranceMinorUnits));
        }

        return report;
    }

    // WHY: EF Core materialization only. _lines is initialised at its field; Country is
    // populated by EF via the value conversion.
    private VendorComparisonReport()
    {
    }
}
