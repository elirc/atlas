namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/client-orgs.</summary>
// WHY: Name is nullable on purpose — absence is a VALID request shape that the
// domain must reject with a useful message, not a deserialization 500. The contract
// accepts what clients might send; the domain decides what's acceptable.
public sealed record CreateClientOrgRequest(string? Name);
