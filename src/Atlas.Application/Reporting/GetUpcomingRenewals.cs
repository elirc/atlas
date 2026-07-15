using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Application.Reporting;

/// <summary>The window a renewals query runs over, once authorization has already passed.</summary>
public sealed record RenewalWindow(Guid OrgId, DateOnly Through);

/// <summary>
/// Upcoming document renewals/expiries in the next N days — the compliance officer's Monday view, and
/// the streaming CSV export. Serves two shapes of the same query: a buffered list (JSON) and a stream
/// (CSV).
/// </summary>
public sealed class GetUpcomingRenewals(IReportQueries reports, ICurrentContext context, TimeProvider clock)
{
    public const int DefaultWindowDays = 90;
    public const int MaxWindowDays = 365;

    public async Task<IReadOnlyList<UpcomingRenewalRow>> ExecuteAsync(
        int? withinDays, CancellationToken cancellationToken)
    {
        RenewalWindow window = Authorize(withinDays);
        return await reports.UpcomingRenewalsAsync(window.OrgId, window.Through, cancellationToken);
    }

    /// <summary>
    /// Run authorization and validation EAGERLY and return the window. The CSV endpoint calls this
    /// first, before it writes a single byte, so a 403 or a 400 is a clean problem+json response —
    /// not a failure discovered halfway through a stream whose headers are already on the wire.
    /// </summary>
    // ⚠️ THE ERROR CONTRACT OF A STREAM. Once the CSV endpoint has written its header row the HTTP
    // status is committed; there is no taking it back to a 400. So everything that CAN be a clean
    // error — is the caller allowed here? is the window valid? — MUST happen before streaming starts.
    // This method is that "before". What remains possible mid-stream is only an infrastructure fault
    // (the DB connection drops at row 5000), and its honest outcome is a truncated body that the
    // client detects as malformed CSV, plus a server log — never a 200 that quietly lost rows.
    public RenewalWindow Authorize(int? withinDays)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        int window = withinDays ?? DefaultWindowDays;
        if (window < 1 || window > MaxWindowDays)
        {
            throw new RequestValidationException($"withinDays must be between 1 and {MaxWindowDays}.");
        }

        DateOnly through = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(window);
        return new RenewalWindow(orgId, through);
    }

    /// <summary>The streaming half — rows flow straight from the projection; nothing is buffered.</summary>
    public IAsyncEnumerable<UpcomingRenewalRow> StreamAsync(
        RenewalWindow window, CancellationToken cancellationToken) =>
        reports.StreamUpcomingRenewalsAsync(window.OrgId, window.Through, cancellationToken);
}
