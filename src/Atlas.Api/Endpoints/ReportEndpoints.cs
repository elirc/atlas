using System.Globalization;
using System.Text;
using Atlas.Api.Mapping;
using Atlas.Application.Abstractions;
using Atlas.Application.Reporting;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

// LEARN: reports live under their own /reports group, not hung off the resources they summarise. A
// cost report is nobody's sub-resource — it crosses workers, runs, and payslips — so it gets a
// first-class home, the same call ApprovalEndpoints made for the cross-type inbox. Every report is a
// GET with explicit query params and an org-scoped, role-gated use case behind it.
public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReports(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/headcount", async (
            DateOnly? asOf, GetHeadcountReport useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(asOf, ct)).Select(r => r.ToDto())))
        .Produces<IEnumerable<HeadcountRowDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/employment-cost", async (
            int? year, int? month, GetEmploymentCostReport useCase, TimeProvider clock, CancellationToken ct) =>
        {
            // Default to the current UTC month, derived ONCE at the edge from the injected clock — the
            // sanctioned way to touch "now" (the WorkerEndpoints pattern; the domain stays clockless).
            DateOnly now = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var rows = await useCase.ExecuteAsync(year ?? now.Year, month ?? now.Month, ct);
            return Results.Ok(rows.Select(r => r.ToDto()));
        })
        .Produces<IEnumerable<EmploymentCostRowDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/renewals", async (
            int? withinDays, GetUpcomingRenewals useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(withinDays, ct)).Select(r => r.ToDto())))
        .Produces<IEnumerable<UpcomingRenewalDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/renewals.csv", StreamRenewalsCsv)
        .Produces(StatusCodes.Status200OK, contentType: "text/csv")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/compliance-roster", async (
            DateOnly? asOf, GetComplianceRoster useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(asOf, ct)).Select(r => r.ToDto())))
        .Produces<IEnumerable<ComplianceRosterRowDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // Reference data — the same for every org, safe to share and safe to cache (ISupportedCountries
        // is decorated by the memory cache). Auth-gated but not role-gated: country codes are not
        // secret, but we still refuse anonymous callers to keep the whole API behind a token.
        group.MapGet("/meta/countries", async (
            ISupportedCountries countries, ICurrentContext context, CancellationToken ct) =>
        {
            context.RequireUser();
            return Results.Ok((await countries.AllAsync(ct)).Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<SupportedCountryDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }

    // ⚠️ THE STREAM'S ERROR CONTRACT, IN CODE. Authorize() runs FIRST and eagerly — a 403 or a 400 is
    // a clean problem+json here, before a single byte of CSV is written. Only AFTER that do we commit
    // the response (content-type + header row). From that point the HTTP status is frozen at 200: if
    // the reader faults at row 5000, we cannot retroactively send a 500 — the honest failure is a
    // truncated body the client sees as malformed CSV, plus a server-side log. The design's answer to
    // "what happens at row 10 when row 11 throws" is: everything that COULD be a clean error already
    // happened before row 1. Memory stays bounded because rows stream off the reader one at a time.
    private static async Task StreamRenewalsCsv(
        int? withinDays, GetUpcomingRenewals useCase, HttpResponse response, CancellationToken ct)
    {
        RenewalWindow window = useCase.Authorize(withinDays);

        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = "attachment; filename=\"renewals.csv\"";

        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync("worker_id,worker_name,country,kind,reference,expires_on");

        await foreach (UpcomingRenewalRow row in useCase.StreamAsync(window, ct).WithCancellation(ct))
        {
            string line =
                $"{row.WorkerId}," +
                $"{CsvField(row.WorkerName)}," +
                $"{row.Country.Value}," +
                $"{row.Kind}," +
                $"{CsvField(row.Reference)}," +
                $"{row.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(ct);
    }

    // Minimal RFC-4180 escaping: quote a field only if it contains a comma, quote, or newline, and
    // double any embedded quotes. Worker names and references are free text — never trust them raw.
    private static string CsvField(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
