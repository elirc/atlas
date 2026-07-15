namespace Atlas.Application.Operations;

/// <summary>
/// The result of a readiness probe: the overall verdict the HTTP status is drawn from, plus the
/// individual dependency checks behind it. A record, not an entity — it is a read-only snapshot the
/// endpoint maps straight to a DTO.
/// </summary>
public sealed record ReadinessReport(bool IsReady, IReadOnlyList<ReadinessCheck> Checks);

/// <summary>
/// One dependency's contribution to readiness. <see cref="Status"/> is a deliberately tiny
/// vocabulary — <c>"ok"</c> | <c>"degraded"</c> | <c>"down"</c> — so the JSON stays greppable and an
/// operator's eye lands on the one line that is not "ok". <see cref="Detail"/> carries the numbers
/// (pending depth, lag seconds) that a human needs to decide what to do next.
/// </summary>
public sealed record ReadinessCheck(string Name, string Status, string Detail);
