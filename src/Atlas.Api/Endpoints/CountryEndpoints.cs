using Atlas.Api.Mapping;
using Atlas.Application.Countries;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

public static class CountryEndpoints
{
    // LEARN: an endpoint file should read like a table of contents: route → use case
    // → contract. If you find yourself scrolling here, logic has leaked upward.
    public static RouteGroupBuilder MapCountries(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/countries").WithTags("Countries");

        group.MapGet("/", async (ListCountries useCase, CancellationToken ct) =>
        {
            var countries = await useCase.ExecuteAsync(ct);
            return Results.Ok(countries.Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<CountryDto>>();

        group.MapGet("/{code}", async (string code, GetCountry useCase, CancellationToken ct) =>
        {
            var country = await useCase.ExecuteAsync(code, ct);
            return Results.Ok(country.ToDto());
        })
        .Produces<CountryDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
