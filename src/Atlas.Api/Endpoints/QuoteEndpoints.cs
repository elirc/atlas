using Atlas.Api.Mapping;
using Atlas.Application.Pagination;
using Atlas.Application.Quotes;
using Atlas.Contracts.V1;
using Atlas.Domain.Quotes;

namespace Atlas.Api.Endpoints;

public static class QuoteEndpoints
{
    // LEARN: route → use case → contract, nothing else. The pricing lives in the Domain,
    // the authorization lives in the use case; this file only translates HTTP.
    public static RouteGroupBuilder MapQuotes(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/quotes").WithTags("Quotes");

        group.MapPost("/", async (
            CreateQuoteRequest request,
            CreateQuote useCase,
            CancellationToken ct) =>
        {
            Quote quote = await useCase.ExecuteAsync(request.CountryCode, request.Gross, ct);

            // 201 + Location — "you made a thing, here is where it lives."
            return Results.Created($"/api/v1/quotes/{quote.Id}", quote.ToDto());
        })
        .Produces<QuoteDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (Guid id, GetQuote useCase, CancellationToken ct) =>
        {
            Quote quote = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(quote.ToDto());
        })
        .Produces<QuoteDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListQuotes useCase,
            CancellationToken ct) =>
        {
            Page<Quote> page = await useCase.ExecuteAsync(cursor, limit, ct);

            // Next-page token rides the X-Next-Cursor header; the body stays a plain array.
            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(q => q.ToDto()));
        })
        .Produces<IEnumerable<QuoteDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
