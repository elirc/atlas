namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/workers/{id}/documents. Kind is an enum name (e.g. "WorkAuthorization").</summary>
public sealed record RecordDocumentRequest(string? Kind, string? Reference, DateOnly? ExpiresOn);

/// <summary>A recorded worker document (metadata only — never the file).</summary>
public sealed record WorkerDocumentDto(
    Guid Id,
    Guid WorkerId,
    string Kind,
    string Reference,
    DateOnly? ExpiresOn,
    DateTimeOffset RecordedAtUtc);

/// <summary>One required document's compliance status for a worker.</summary>
public sealed record DocumentComplianceLineDto(
    string Kind,
    string Description,
    bool ExpiryTracked,
    string State,
    DateOnly? ExpiresOn);

/// <summary>A worker's compliance rollup, served by GET /api/v1/workers/{id}/compliance.</summary>
public sealed record WorkerComplianceDto(
    Guid WorkerId,
    string Country,
    bool IsCompliant,
    IReadOnlyList<DocumentComplianceLineDto> Documents);

/// <summary>One row of the org-wide expiring-documents view.</summary>
public sealed record ExpiringDocumentDto(
    Guid Id,
    Guid WorkerId,
    string Kind,
    string Reference,
    DateOnly ExpiresOn);
