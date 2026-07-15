using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;

namespace Atlas.Application.Compliance;

/// <summary>
/// Record a compliance document for a worker (metadata + optional expiry, never the binary). HR/admin
/// only; the worker must belong to the caller's org (tenant-safe by construction — we load through
/// FindInOrgAsync, not a bare primary-key read).
/// </summary>
public sealed class RecordWorkerDocument(
    IWorkerRepository workers,
    IWorkerDocumentRepository documents,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WorkerDocument> ExecuteAsync(
        Guid workerId, string? kind, string? reference, DateOnly? expiresOn, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // Parse-and-reject, like the invoice status filter: an unknown kind is a client mistake (400),
        // not a silent default.
        if (!Enum.TryParse(kind, ignoreCase: true, out DocumentKind parsedKind) || !Enum.IsDefined(parsedKind))
        {
            throw new RequestValidationException($"'{kind}' is not a known document kind.");
        }

        _ = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        DateTimeOffset now = clock.GetUtcNow();
        WorkerDocument document = WorkerDocument.Record(orgId, workerId, parsedKind, reference, expiresOn, now);
        await documents.AddAsync(document, cancellationToken);

        string details = JsonSerializer.Serialize(new { Kind = parsedKind.ToString(), document.Reference, document.ExpiresOn });
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "worker.document_recorded", "worker", workerId, details, now));

        await events.PublishAsync(
            new AtlasEvent("worker.document_recorded", orgId, "worker", workerId, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
        return document;
    }
}
