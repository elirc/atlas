using Atlas.Contracts.V1;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Orgs;
using Atlas.Domain.Workforce;

namespace Atlas.Api.Mapping;

// WHY: hand-written, boring, and in the Api layer — mapping is the Api's JOB
// (translating domain ↔ contract), not a domain concern and not worth a library
// at this size. If this file ever hurts, that pain is data (see ADR-0001).
internal static class DtoMapping
{
    public static CountryDto ToDto(this Country country) =>
        new(country.Code.Value, country.Name, country.Currency);

    public static ClientOrgDto ToDto(this ClientOrg org) =>
        new(org.Id, org.Name, org.CreatedAtUtc);

    // WHY: `asOf` is passed in — the endpoint derives it once from the injected
    // TimeProvider (the edge), so the projection is deterministic and testable and
    // the domain never reads an ambient clock. See the ambient-time ban.
    public static WorkerDto ToDto(this Worker worker, DateOnly asOf) =>
        new(worker.Id, worker.FullName, worker.ContactEmail.Value, worker.Country.Value,
            worker.HiredOn, worker.ProbationEndsOn, worker.IsInProbationOn(asOf), worker.CreatedAtUtc);

    public static EmploymentTermsDto ToDto(this EmploymentRecord record) =>
        new(record.Id, record.EffectiveFrom, record.JobTitle, record.AnnualSalary,
            record.SalaryCurrency, record.WeeklyHours);

    public static AuditEntryDto ToDto(this AuditEntry entry) =>
        new(entry.Id, entry.Action, entry.SubjectType, entry.SubjectId,
            entry.ActorUserId, entry.DetailsJson, entry.AtUtc);
}
