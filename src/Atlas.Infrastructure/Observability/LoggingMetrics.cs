using Atlas.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Observability;

/// <summary>
/// The log-based <see cref="IMetrics"/> adapter: a metric IS a structured log line. A log pipeline
/// (or the dev console) aggregates by <c>{Metric}</c>; there is no APM SDK, no exporter, no backend
/// to run in CI. This is the honest floor of observability — the questions ("what is slow? what is
/// backing up?") are askable today, and the ceiling (real metrics infra) is one adapter away.
/// </summary>
// 🔗 See ADR-0018 and the S15 debate: the destination (proper metrics) is agreed; the *timing* is
//    the argument. Shipping the port + this adapter makes "swapping it is an S12-grade local diff" a
//    fact you can check, not a promise.
internal sealed class LoggingMetrics(ILogger<LoggingMetrics> logger) : IMetrics
{
    private static readonly IReadOnlyDictionary<string, string> NoTags = new Dictionary<string, string>();

    public void Record(
        string name,
        double value,
        string unit = "count",
        IReadOnlyDictionary<string, string>? tags = null)
    {
        // A single, machine-parseable shape: metric <name>=<value><unit> {tags}. The {@Tags}
        // structured value keeps the dimensions queryable instead of smeared into the message string.
        logger.LogInformation(
            "metric {Metric}={Value}{Unit} {@Tags}", name, value, unit, tags ?? NoTags);
    }
}
