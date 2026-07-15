using Atlas.Application.Abstractions;

namespace Atlas.Api.Observability;

/// <summary>
/// Per-request telemetry: assign (or accept) a correlation id, open a logging scope so EVERY log
/// line emitted while handling the request carries it, echo the id back on the response, and record
/// the request's duration through <see cref="IMetrics"/>. One id, one request, one trace — the
/// thread you pull to follow a business event from the HTTP call through the outbox row it enqueued.
/// </summary>
// 📘 Observability is a data-modeling problem wearing an ops costume. The correlation id is just a
//    key you carry so the logs of one request JOIN. It is added here, at the edge, because the edge
//    is where a request becomes traceable — and because an inbound id is untrusted input (below).
// 🔗 The next increment (recorded in ADR-0018 and the retrospective's open-debts) threads this id
//    onto the OutboxMessage row and the webhook delivery, so one id spans request → event → webhook.
//    That needs a column + migration; S15 ships the request half and names the rest.
public sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    IMetrics metrics,
    TimeProvider clock,
    ILogger<RequestTelemetryMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string CorrelationItemKey = "atlas.correlation_id";
    private const int MaxCorrelationLength = 128;

    public async Task InvokeAsync(HttpContext http)
    {
        string correlationId = ResolveCorrelationId(http);
        http.Items[CorrelationItemKey] = correlationId;
        http.Response.Headers[CorrelationHeader] = correlationId;

        long startedAt = clock.GetTimestamp();

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                await next(http);
            }
            finally
            {
                // Recorded in a finally so a thrown request is still measured — a slow path that
                // always 500s is exactly the one you want a timing on.
                TimeSpan elapsed = clock.GetElapsedTime(startedAt);
                metrics.Record(
                    "http.server.request.duration",
                    elapsed.TotalMilliseconds,
                    "ms",
                    new Dictionary<string, string>
                    {
                        ["method"] = http.Request.Method,
                        ["status"] = http.Response.StatusCode.ToString(),
                    });
            }
        }
    }

    private static string ResolveCorrelationId(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue(CorrelationHeader, out var incoming)
            && incoming.ToString() is { Length: > 0 } value)
        {
            // ⚠️ An inbound correlation id is attacker-controlled. Bound its length and strip control
            //    characters so it cannot forge or wrap log lines (log injection). Trust the id to
            //    correlate; never trust it as data.
            return Sanitize(value);
        }

        return Guid.NewGuid().ToString("n");
    }

    private static string Sanitize(string value)
    {
        string bounded = value.Length > MaxCorrelationLength ? value[..MaxCorrelationLength] : value;
        return new string(bounded.Where(c => !char.IsControl(c)).ToArray());
    }
}
