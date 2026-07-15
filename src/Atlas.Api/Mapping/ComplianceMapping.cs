using Atlas.Application.Compliance;
using Atlas.Contracts.V1;
using Atlas.Domain.Compliance;

namespace Atlas.Api.Mapping;

internal static class ComplianceMapping
{
    public static WorkerDocumentDto ToDto(this WorkerDocument d) =>
        new(d.Id, d.WorkerId, d.Kind.ToString(), d.Reference, d.ExpiresOn, d.RecordedAtUtc);

    public static WorkerComplianceDto ToDto(this WorkerComplianceResult result) =>
        new(
            result.Worker.Id,
            result.Worker.Country.Value,
            result.Report.IsCompliant,
            [.. result.Report.Lines.Select(l => new DocumentComplianceLineDto(
                l.Kind.ToString(), l.Description, l.ExpiryTracked, l.State.ToString(), l.ExpiresOn))]);

    public static ExpiringDocumentDto ToExpiringDto(this WorkerDocument d) =>
        new(d.Id, d.WorkerId, d.Kind.ToString(), d.Reference, d.ExpiresOn!.Value);
}
