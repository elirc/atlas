using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// Append an effective-dated vendor assignment for the caller's org and a country: "from this
/// day, this country's payroll is calculated by this vendor." Records are appended, never
/// edited — the switch schedule is immutable, and a run already in flight keeps its own pinned
/// vendor (see PayrollRun.SelectedVendor).
/// </summary>
// LEARN: this is UpdateEmploymentTerms wearing a different noun (S03). It VALIDATES, APPENDS,
// AUDITS, and COMMITS in one unit of work — no "current vendor" column to overwrite, no closing
// of a prior record. The only real guard beyond the domain's own is the one-record-per-effective-
// day duplicate check, which the domain reports as a friendly 422 and a unique index makes true
// under a race (ADR-0004, second application).
public sealed class AssignPayrollVendor(
    IVendorAssignmentRepository assignments,
    ICountryRepository countries,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<VendorAssignment> ExecuteAsync(
        string? country,
        PayrollVendor vendor,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        // WHY OrgAdmin: choosing who computes your payroll is a high-authority act — it changes
        // the numbers, so it sits with the same role that approves and pays runs.
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        CountryCode countryCode = CountryCode.From(country ?? string.Empty);

        // A vendor can only be assigned for a country Atlas actually supports (has a rule pack
        // for) — otherwise the pinned vendor would have nothing to price against at prepare time.
        if (await countries.FindAsync(countryCode, cancellationToken) is null)
        {
            throw new NotFoundException("country", countryCode.Value);
        }

        // Same-day corrections are a deliberate future workflow (the S15 runbook), not an
        // accidental double-submit; refuse a duplicate effective day with a clear 422.
        if (await assignments.FindByEffectiveDayAsync(orgId, countryCode, effectiveFrom, cancellationToken)
            is not null)
        {
            throw new DomainRuleException(
                $"A vendor assignment already exists for {countryCode.Value} effective {effectiveFrom:yyyy-MM-dd}.");
        }

        DateTimeOffset now = clock.GetUtcNow();

        VendorAssignment assignment = VendorAssignment.Create(
            orgId, countryCode, effectiveFrom, vendor, context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.vendor_assigned", "vendor_assignment", assignment.Id,
            JsonSerializer.Serialize(new
            {
                Country = countryCode.Value,
                Vendor = vendor.ToString(),
                EffectiveFrom = effectiveFrom,
            }),
            now));

        // Announce the scheduled switch so downstream systems (and, in S10, webhooks) can react —
        // published BEFORE commit; the S10 outbox makes that ordering correct (ADR-0011).
        await events.PublishAsync(
            new AtlasEvent(
                "payroll.vendor_assigned", orgId, "vendor_assignment", assignment.Id,
                JsonSerializer.Serialize(new { Country = countryCode.Value, Vendor = vendor.ToString(), EffectiveFrom = effectiveFrom }),
                now),
            cancellationToken);

        await assignments.AddAsync(assignment, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return assignment;
    }
}
