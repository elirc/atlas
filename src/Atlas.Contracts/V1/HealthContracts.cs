namespace Atlas.Contracts.V1;

/// <summary>
/// Liveness payload: the process is up. Intentionally trivial — a liveness probe must not depend on
/// anything downstream (see the ⚠️ in HealthEndpoints), so there is nothing here but a word.
/// </summary>
public sealed record HealthDto(string Status);

/// <summary>
/// Readiness payload: the overall verdict (<c>"ready"</c> / <c>"not-ready"</c>) plus a line per
/// dependency so an operator reading a 503 sees <em>which</em> check failed without opening logs.
/// </summary>
public sealed record ReadinessDto(string Status, IReadOnlyList<ReadinessCheckDto> Checks);

/// <summary>One dependency's readiness line: name, a small status word, and the numbers behind it.</summary>
public sealed record ReadinessCheckDto(string Name, string Status, string Detail);
