using Atlas.Application.Abstractions;
using Atlas.Application.Reporting;
using Atlas.Contracts.V1;

namespace Atlas.Api.Mapping;

// WHY: report rows are already flat projections, so mapping is a near-identity — the row's shape and
// the DTO's shape are almost the same on purpose (the read model WAS designed for the wire). The one
// job here is turning domain value types (CountryCode, DocumentKind) into wire strings at the edge.
internal static class ReportMapping
{
    public static HeadcountRowDto ToDto(this HeadcountRow r) =>
        new(r.Country, r.Total, r.InProbation, r.Active);

    public static EmploymentCostRowDto ToDto(this EmploymentCostRow r) =>
        new(r.Country, r.Currency, r.WorkerCount, r.Gross, r.EmployerBurden, r.Net);

    public static UpcomingRenewalDto ToDto(this UpcomingRenewalRow r) =>
        new(r.WorkerId, r.WorkerName, r.Country.Value, r.Kind.ToString(), r.Reference, r.ExpiresOn!.Value);

    public static ComplianceRosterRowDto ToDto(this ComplianceRosterRow r) =>
        new(r.WorkerId, r.WorkerName, r.Country, r.IsCompliant, r.Outstanding, r.ExpiringSoon);

    public static SupportedCountryDto ToDto(this SupportedCountry c) =>
        new(c.Code, c.Name, c.Currency);
}
