using Atlas.Application.Operations;
using Atlas.Contracts.V1;

namespace Atlas.Api.Mapping;

/// <summary>
/// Application readiness result → wire DTO. The two shapes are kept apart on purpose: the report is
/// an internal snapshot; the DTO is the contract an orchestrator scrapes. Same discipline as every
/// other resource in this codebase (ADR-0002).
/// </summary>
internal static class HealthMapping
{
    public static ReadinessDto ToDto(this ReadinessReport report) =>
        new(
            report.IsReady ? "ready" : "not-ready",
            report.Checks.Select(c => new ReadinessCheckDto(c.Name, c.Status, c.Detail)).ToList());
}
