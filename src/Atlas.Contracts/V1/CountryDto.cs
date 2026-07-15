namespace Atlas.Contracts.V1;

/// <summary>A country Atlas can employ in, as served by /api/v1/countries.</summary>
// LEARN: Contracts records are the API's public vocabulary — primitives only, no
// domain types. This file changing IS the API changing; treat every edit here as a
// compatibility decision (see ADR-0002).
public sealed record CountryDto(string Code, string Name, string Currency);
