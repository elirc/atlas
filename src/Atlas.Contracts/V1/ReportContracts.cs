namespace Atlas.Contracts.V1;

/// <summary>One country's headcount, served by GET /api/v1/reports/headcount.</summary>
public sealed record HeadcountRowDto(string Country, int Total, int InProbation, int Active);

/// <summary>One country+currency's monthly employment cost, served by GET /api/v1/reports/employment-cost.</summary>
public sealed record EmploymentCostRowDto(
    string Country,
    string Currency,
    int WorkerCount,
    decimal Gross,
    decimal EmployerBurden,
    decimal Net);

/// <summary>One upcoming document renewal/expiry, served by GET /api/v1/reports/renewals (JSON and CSV).</summary>
public sealed record UpcomingRenewalDto(
    Guid WorkerId,
    string WorkerName,
    string Country,
    string Kind,
    string Reference,
    DateOnly ExpiresOn);

/// <summary>One worker's line in the org compliance roster, served by GET /api/v1/reports/compliance-roster.</summary>
public sealed record ComplianceRosterRowDto(
    Guid WorkerId,
    string WorkerName,
    string Country,
    bool IsCompliant,
    int Outstanding,
    int ExpiringSoon);

/// <summary>A supported country as reference data, served by GET /api/v1/reports/meta/countries.</summary>
public sealed record SupportedCountryDto(string Code, string Name, string Currency);
