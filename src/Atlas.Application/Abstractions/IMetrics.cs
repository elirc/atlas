namespace Atlas.Application.Abstractions;

/// <summary>
/// A tiny operational-metrics seam. The only implementation this course ships is
/// <c>LoggingMetrics</c> — each metric becomes one structured log line, aggregated by a log
/// pipeline. No APM vendor, no OpenTelemetry backend: the deliberate S15 choice (ADR-0018). The
/// value of the metric is the <em>question</em> it answers ("what would page us?"), which you can
/// ask with logs today; buying the dashboard is a swap of this one file when the questions have
/// earned it.
/// </summary>
// 📘 This is the interface the S15 design debate is about. "OTel without a backend is ceremony;
//    log-derived metrics teach the questions before buying the answers." Because IMetrics is one
//    file behind a port, replacing LoggingMetrics with an OTel/StatsD adapter is an S12-grade local
//    diff — a checkable claim, not a promise.
public interface IMetrics
{
    /// <summary>
    /// Record a single measurement. <paramref name="unit"/> is free text ("ms", "count", "rows");
    /// <paramref name="tags"/> are low-cardinality dimensions (method, status) — never an id.
    /// </summary>
    void Record(
        string name,
        double value,
        string unit = "count",
        IReadOnlyDictionary<string, string>? tags = null);
}
