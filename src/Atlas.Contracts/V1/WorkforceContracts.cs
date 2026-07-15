namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/workers — hire a worker with initial terms.</summary>
public sealed record HireWorkerRequest(
    string? FullName,
    string? ContactEmail,
    string? Country,
    DateOnly HiredOn,
    string? JobTitle,
    decimal AnnualSalary,
    decimal WeeklyHours);

/// <summary>Body of POST /api/v1/workers/{id}/terms — append effective-dated terms.</summary>
public sealed record UpdateTermsRequest(
    DateOnly EffectiveFrom,
    string? JobTitle,
    decimal AnnualSalary,
    decimal WeeklyHours);

public sealed record WorkerDto(
    Guid Id,
    string FullName,
    string ContactEmail,
    string Country,
    DateOnly HiredOn,
    DateOnly ProbationEndsOn,
    bool InProbation,
    DateTimeOffset CreatedAtUtc);

public sealed record EmploymentTermsDto(
    Guid RecordId,
    DateOnly EffectiveFrom,
    string JobTitle,
    decimal AnnualSalary,
    string SalaryCurrency,
    decimal WeeklyHours);

/// <summary>GET /api/v1/workers/{id} — profile plus the terms in force today.</summary>
public sealed record WorkerDetailDto(WorkerDto Worker, EmploymentTermsDto? CurrentTerms);

/// <summary>GET /api/v1/workers/{id}/terms?asOf= — point-in-time terms plus full history.</summary>
public sealed record WorkerTermsDto(
    Guid WorkerId,
    DateOnly AsOf,
    EmploymentTermsDto? Effective,
    IReadOnlyList<EmploymentTermsDto> History);

public sealed record AuditEntryDto(
    Guid Id,
    string Action,
    string SubjectType,
    Guid SubjectId,
    Guid ActorUserId,
    string DetailsJson,
    DateTimeOffset AtUtc);
