using Atlas.Api.Mapping;
using Atlas.Application.Expenses;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Expenses;

namespace Atlas.Api.Endpoints;

// LEARN: the expense endpoints are a near-mirror of the leave endpoints — same named-transition
// shape (approve / deny / cancel are POSTs, not a PATCH-to-status; ADR-0006), same cursor list.
// That symmetry is not an accident: both flows are the SAME approval lifecycle, which is exactly
// why S08 extracted the domain component underneath them. Reimbursement has no endpoint yet — it
// moves money, so it waits for the payment provider (S09) and the idempotency middleware (S10).
public static class ExpenseEndpoints
{
    public static RouteGroupBuilder MapExpenses(this RouteGroupBuilder api)
    {
        RouteGroupBuilder expenses = api.MapGroup("/expenses").WithTags("Expenses");

        expenses.MapPost("/", async (
            SubmitExpenseRequest request,
            SubmitExpense useCase,
            CancellationToken ct) =>
        {
            ExpenseClaim created = await useCase.ExecuteAsync(
                request.WorkerId,
                request.Amount?.Amount ?? 0m,
                request.Amount?.Currency,
                request.Category,
                request.ReceiptUrl,
                request.ReceiptFilename,
                request.Note,
                ct);

            return Results.Created($"/api/v1/expenses/{created.Id}", created.ToDto());
        })
        .Produces<ExpenseClaimDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        expenses.MapPost("/{id:guid}/approve", async (
            Guid id, ApproveExpense useCase, CancellationToken ct) =>
        {
            ExpenseClaim decided = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(decided.ToDto());
        })
        .Produces<ExpenseClaimDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        expenses.MapPost("/{id:guid}/deny", async (
            Guid id, DenyExpenseRequest? request, DenyExpense useCase, CancellationToken ct) =>
        {
            ExpenseClaim decided = await useCase.ExecuteAsync(id, request?.Reason, ct);
            return Results.Ok(decided.ToDto());
        })
        .Produces<ExpenseClaimDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        expenses.MapPost("/{id:guid}/cancel", async (
            Guid id, CancelExpense useCase, CancellationToken ct) =>
        {
            ExpenseClaim cancelled = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(cancelled.ToDto());
        })
        .Produces<ExpenseClaimDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        expenses.MapGet("/{id:guid}", async (
            Guid id, GetExpense useCase, CancellationToken ct) =>
        {
            ExpenseClaim claim = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(claim.ToDto());
        })
        .Produces<ExpenseClaimDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        expenses.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListExpenses useCase,
            CancellationToken ct) =>
        {
            Page<ExpenseClaim> page = await useCase.ExecuteAsync(cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<ExpenseClaimDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
