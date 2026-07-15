using Atlas.Contracts.V1;
using Atlas.Domain.Countries;
using Atlas.Domain.Orgs;

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
}
