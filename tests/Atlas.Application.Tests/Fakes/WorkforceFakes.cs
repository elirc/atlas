using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Auditing;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryWorkerRepository : IWorkerRepository
{
    public List<Worker> Rows { get; } = [];

    public Task AddAsync(Worker worker, CancellationToken ct)
    {
        Rows.Add(worker);
        return Task.CompletedTask;
    }

    public Task<Worker?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(w => w.Id == id));

    public Task<Worker?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(w => w.Id == id && w.OrgId == orgId));

    public Task<IReadOnlyList<Worker>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<Worker> ordered = Rows
            .Where(w => w.OrgId == orgId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ThenByDescending(w => w.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(w =>
                w.CreatedAtUtc < cursor.CreatedAtUtc
                || (w.CreatedAtUtc == cursor.CreatedAtUtc
                    && w.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<Worker> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}

public sealed class InMemoryEmploymentRecordRepository : IEmploymentRecordRepository
{
    public List<EmploymentRecord> Rows { get; } = [];

    public Task AddAsync(EmploymentRecord record, CancellationToken ct)
    {
        Rows.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EmploymentRecord>> ListForWorkerAsync(
        Guid workerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmploymentRecord>>(
            [.. Rows.Where(r => r.WorkerId == workerId)]);
}

public sealed class RecordingAuditTrail : IAuditTrail
{
    public List<AuditEntry> Entries { get; } = [];

    public void Append(AuditEntry entry) => Entries.Add(entry);

    public Task<IReadOnlyList<AuditEntry>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<AuditEntry> ordered = Entries
            .Where(e => e.OrgId == orgId)
            .OrderByDescending(e => e.AtUtc)
            .ThenByDescending(e => e.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(e =>
                e.AtUtc < cursor.CreatedAtUtc
                || (e.AtUtc == cursor.CreatedAtUtc
                    && e.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<AuditEntry> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}
