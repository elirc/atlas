using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Engagements;

/// <summary>The Worker and initial terms an activation created, plus the engagement.</summary>
public sealed record ActivationOutcome(Engagement Engagement, Worker Worker, EmploymentRecord InitialTerms);

/// <summary>
/// Onboarding → Active — and, in the SAME commit, the moment an engagement becomes
/// a real employee: a <see cref="Worker"/> and its initial
/// <see cref="EmploymentRecord"/>, hired from the terms this engagement froze at
/// creation. One transaction spans the state change, the worker, the terms, and the
/// audit row; if any write fails, none of them happened.
/// </summary>
public sealed class ActivateEngagement(
    IEngagementRepository engagements,
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ActivationOutcome> ExecuteAsync(
        Guid engagementId,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement engagement =
            await engagements.FindInOrgAsync(engagementId, orgId, cancellationToken)
            ?? throw new NotFoundException("engagement", engagementId);

        DateTimeOffset now = clock.GetUtcNow();

        // WHY: the guard runs FIRST — Activate throws 422 if the checklist isn't
        // finished or the state is wrong, before a single Worker object is built.
        // No half-hire is ever constructed, let alone committed.
        EngagementActivation activation = engagement.Activate(now);

        DateOnly hiredOn = DateOnly.FromDateTime(now.UtcDateTime);

        var worker = Worker.Hire(
            orgId, activation.CandidateName, activation.CandidateEmail,
            activation.Country, hiredOn, now);

        // The initial terms come from the quote data the engagement snapshot froze —
        // priced in the country's currency, effective from the hire date.
        var terms = EmploymentRecord.Create(
            worker.Id, hiredOn, activation.RoleTitle, activation.AnnualSalary,
            activation.SalaryCurrency, activation.WeeklyHours, context.UserId, now);

        engagement.RecordCreatedWorker(worker.Id);

        string details = JsonSerializer.Serialize(new
        {
            WorkerId = worker.Id,
            activation.RoleTitle,
            activation.AnnualSalary,
            activation.SalaryCurrency,
            HiredOn = hiredOn,
        });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.activated", "engagement", engagement.Id, details, now));

        await workers.AddAsync(worker, cancellationToken);
        await records.AddAsync(terms, cancellationToken);

        // Announce the activation, then commit the engagement, worker, terms, and
        // audit row together — four writes, one transaction.
        await events.PublishAsync(
            new AtlasEvent("engagement.activated", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new ActivationOutcome(engagement, worker, terms);
    }
}
