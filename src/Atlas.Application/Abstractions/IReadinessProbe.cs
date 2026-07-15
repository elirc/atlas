using Atlas.Application.Operations;

namespace Atlas.Application.Abstractions;

/// <summary>
/// A readiness probe: <em>should this instance receive traffic right now?</em> Distinct from
/// liveness (is the process alive?) — see the ⚠️ in <c>HealthEndpoints</c>. The port lives in the
/// Application layer because "what makes Atlas ready" is a business decision (the database is a hard
/// dependency; the outbox dispatcher's lag is an operational signal, not a traffic gate), while
/// <em>how</em> to measure it is an Infrastructure concern (querying EF, the outbox table).
/// </summary>
// 🔗 The one implementation, AtlasReadinessProbe (Infrastructure), reads the outbox depth + lag the
//    S10 outbox left as its named health signal (ADR-0011). The runbook that watches this endpoint is
//    docs/runbooks/outbox-dispatcher-lag.md.
public interface IReadinessProbe
{
    Task<ReadinessReport> CheckAsync(CancellationToken cancellationToken);
}
