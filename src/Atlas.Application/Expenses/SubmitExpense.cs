using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Expenses;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Expenses;

/// <summary>
/// Record a worker's expense claim. v1: submitted BY HR/admin ON BEHALF of a worker — the same
/// deferral leave made (no worker↔user link yet, ADR-0007). The claim travels Draft → Submitted
/// inside this one use case, exactly as leave does.
/// </summary>
public sealed class SubmitExpense(
    IWorkerRepository workers,
    IExpenseClaimRepository claims,
    IEventSink events,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ExpenseClaim> ExecuteAsync(
        Guid workerId,
        decimal amount,
        string? currency,
        string? category,
        string? receiptUrl,
        string? receiptFilename,
        string? note,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Worker worker = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        // WHY parse the category HERE (400) rather than let a bad value reach the domain: a
        // misspelled category is a malformed REQUEST, not a broken business rule. The domain
        // still guards the enum (defence in depth); this turns the common case into a clean 400.
        if (!Enum.TryParse(category, ignoreCase: true, out ExpenseCategory parsedCategory)
            || !Enum.IsDefined(parsedCategory))
        {
            throw new RequestValidationException(
                $"'{category}' is not a known expense category.");
        }

        // Money.Of + CurrencyCode.From guard the currency SHAPE (422 on a non-ISO code); the
        // domain guards the amount sign. Currency travels with the amount from the very edge.
        Money money = Money.Of(amount, CurrencyCode.From(currency));

        DateTimeOffset now = clock.GetUtcNow();

        var claim = ExpenseClaim.Draft(
            worker.Id, orgId, money, parsedCategory, receiptUrl, receiptFilename, note,
            context.UserId, now);
        claim.Submit(now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "expense.submitted", "expense_claim", claim.Id,
            JsonSerializer.Serialize(new
            {
                WorkerId = worker.Id,
                Amount = money.Amount,
                Currency = money.Currency.Value,
                Category = parsedCategory.ToString(),
            }),
            now));

        await events.PublishAsync(
            new AtlasEvent(
                "expense.submitted", orgId, "expense_claim", claim.Id,
                JsonSerializer.Serialize(new
                {
                    WorkerId = worker.Id,
                    Amount = money.Amount,
                    Currency = money.Currency.Value,
                    Category = parsedCategory.ToString(),
                    SubmittedAtUtc = now,
                }),
                now),
            cancellationToken);

        await claims.AddAsync(claim, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return claim;
    }
}
