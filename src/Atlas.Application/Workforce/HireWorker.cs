using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

public sealed record HiredWorker(Worker Worker, EmploymentRecord InitialTerms);

public sealed class HireWorker(
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    ICountryRepository countries,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<HiredWorker> ExecuteAsync(
        string? fullName,
        string? contactEmail,
        string? countryCode,
        DateOnly hiredOn,
        string? jobTitle,
        decimal annualSalary,
        decimal weeklyHours,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        CountryCode code = CountryCode.From(countryCode);
        Country country = await countries.FindAsync(code, cancellationToken)
            ?? throw new DomainRuleException($"Atlas does not support employment in '{code}' yet.");

        DateTimeOffset now = clock.GetUtcNow();

        var worker = Worker.Hire(
            orgId, fullName, EmailAddress.From(contactEmail), code, hiredOn, now);

        // WHY: the initial terms are effective from the hire date and priced in the
        // COUNTRY's currency — a worker in Germany is paid in EUR regardless of the
        // client org's home currency (that conversion is a quote/billing concern).
        var terms = EmploymentRecord.Create(
            worker.Id, hiredOn, jobTitle, annualSalary, country.Currency,
            weeklyHours, context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "worker.hired", "worker", worker.Id,
            JsonSerializer.Serialize(new
            {
                worker.FullName,
                Country = code.Value,
                HiredOn = hiredOn,
                terms.JobTitle,
                terms.AnnualSalary,
                terms.SalaryCurrency,
            }),
            now));

        await workers.AddAsync(worker, cancellationToken);
        await records.AddAsync(terms, cancellationToken);

        // LEARN: worker + initial terms + audit row — three writes, one commit.
        // A worker without terms or a hire without an audit entry cannot exist,
        // even for a millisecond of crash window.
        await unitOfWork.CommitAsync(cancellationToken);

        return new HiredWorker(worker, terms);
    }
}
