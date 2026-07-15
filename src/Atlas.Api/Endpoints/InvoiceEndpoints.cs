using Atlas.Api.Mapping;
using Atlas.Application.Invoicing;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Invoicing;

namespace Atlas.Api.Endpoints;

// LEARN: the two approvals and the pay are NAMED endpoints (/hr-approve, /admin-approve, /pay),
// not a PATCH to a status field — ADR-0006's rule. Each state change with business meaning is a
// verb on the resource, carrying its own authz and its own failure modes: /pay can 503 when the
// provider is unreachable; the approvals cannot. A single PATCH would collapse those into one
// undifferentiated response.
public static class InvoiceEndpoints
{
    public static RouteGroupBuilder MapInvoices(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/invoices").WithTags("Invoices");

        group.MapPost("/", async (
            SubmitInvoiceRequest request,
            SubmitInvoice useCase,
            CancellationToken ct) =>
        {
            IReadOnlyList<NewInvoiceLine> lines = request.Lines is null
                ? []
                : [.. request.Lines.Select(l => new NewInvoiceLine(l.Description, l.Amount, l.Currency))];

            ContractorInvoice invoice = await useCase.ExecuteAsync(request.ContractorId, lines, ct);
            return Results.Created($"/api/v1/invoices/{invoice.Id}", invoice.ToDto());
        })
        .Produces<ContractorInvoiceDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/hr-approve", async (
            Guid id, HrApproveInvoice useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<ContractorInvoiceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/admin-approve", async (
            Guid id, AdminApproveInvoice useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<ContractorInvoiceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // WHY 200 for BOTH paid and failed: a declined payment is a recorded OUTCOME (the invoice
        // is now Failed), not a request error — the caller gets the invoice back and reads its
        // status. Only a genuinely unreachable provider is a 503 (retryable).
        group.MapPost("/{id:guid}/pay", async (
            Guid id, PayInvoice useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<ContractorInvoiceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", async (
            Guid id, GetInvoice useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<ContractorInvoiceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            string? status,
            string? cursor,
            int? limit,
            HttpResponse response,
            ListInvoices useCase,
            CancellationToken ct) =>
        {
            Page<ContractorInvoice> page = await useCase.ExecuteAsync(status, cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            // Summary projection, not the full ToDto: the list did not load lines, and Total()
            // folds them — a per-row detail map here would throw on the un-loaded collection.
            return Results.Ok(page.Items.Select(i => i.ToSummaryDto()));
        })
        .Produces<IEnumerable<ContractorInvoiceSummaryDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/reconciliation", async (
            Guid id, ReconcileInvoicePayment useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<InvoiceReconciliationDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }
}
