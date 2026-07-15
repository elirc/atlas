using System.Security.Cryptography;
using System.Text;
using Atlas.Api.Auth;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;

namespace Atlas.Api.Idempotency;

/// <summary>
/// Honors the <c>Idempotency-Key</c> header so a client that retries a money-moving POST (because it
/// timed out, not because it wants to pay twice) gets the ORIGINAL response replayed instead of a
/// second execution. On money-moving routes the header is MANDATORY; elsewhere it is honored if
/// present. This is the transport-level half of harvesting the double-pay flaw — the use case stays
/// a plain "pay once and record it", and the edge guarantees "once".
/// </summary>
// 📘 Why publish "publish before commit" was a bug but "check the key before executing" is the fix:
// the outbox made EVENTS transactional; idempotency makes REQUESTS replay-safe. Both exist because
// the network is allowed to deliver the same thing twice — the outbox can redeliver an event, and a
// client can resend a POST. At-least-once delivery is only safe when the receiver is idempotent; this
// middleware is Atlas being a well-behaved receiver of its own clients' retries. (ADR-0012.)
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(
        HttpContext http,
        RequestContext context,
        IIdempotencyStore store,
        TimeProvider clock)
    {
        HttpRequest request = http.Request;

        // Idempotency is a concern only for writes.
        if (!HttpMethods.IsPost(request.Method))
        {
            await next(http);
            return;
        }

        // ...and only for an authenticated caller: keys are scoped per org, and an anonymous request
        // has no scope. Demanding a key BEFORE auth would answer "401 vs 400" backwards — a caller who
        // isn't logged in should hear "authenticate" (401), not "add a header". So anonymous flows on
        // to the endpoint, whose auth rejects it.
        if (context.OrgId is not { } orgId)
        {
            await next(http);
            return;
        }

        bool mandatory = IsMoneyMoving(request);
        string? key = request.Headers.TryGetValue(HeaderName, out var values)
            && !string.IsNullOrWhiteSpace(values)
                ? values.ToString().Trim()
                : null;

        // No key on a route that demands one is a 400 the (authenticated) client must fix —
        // money-moving without a retry-safety key is not a request we will execute.
        if (key is null)
        {
            if (mandatory)
            {
                throw new RequestValidationException(
                    $"The '{HeaderName}' header is required for this operation.");
            }

            await next(http);
            return;
        }

        // Fingerprint method + path + body so a REUSED key with a DIFFERENT body is caught, not masked.
        request.EnableBuffering();
        string requestHash = await ComputeRequestHashAsync(request, http.RequestAborted);
        request.Body.Position = 0;

        StoredResponse? prior = await store.FindAsync(orgId, key, http.RequestAborted);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(prior.RequestHash), Encoding.ASCII.GetBytes(requestHash)))
            {
                throw new IdempotencyConflictException(
                    $"The idempotency key '{key}' was already used with a different request.");
            }

            await ReplayAsync(http, prior);
            return;
        }

        // First sight of this key: run the pipeline while capturing the response, then remember it —
        // but ONLY if it succeeded. A 4xx/5xx left nothing worth replaying (and a 503 MUST stay
        // retryable, so we deliberately never store it).
        Stream original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await next(http);
        }
        finally
        {
            http.Response.Body = original;
        }

        byte[] body = buffer.ToArray();

        if (http.Response.StatusCode is >= 200 and < 300)
        {
            await store.SaveAsync(
                orgId, key, request.Method, request.Path, requestHash,
                http.Response.StatusCode, http.Response.ContentType, body,
                clock.GetUtcNow(), http.RequestAborted);
        }

        http.Response.ContentLength = body.Length;
        await original.WriteAsync(body, http.RequestAborted);
    }

    // Money-moving POSTs, where a key is mandatory: paying an invoice and executing a payroll run.
    private static bool IsMoneyMoving(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        string path = request.Path.Value ?? string.Empty;
        return path.EndsWith("/pay", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/execute", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeRequestHashAsync(HttpRequest request, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var prefix = Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}\n");
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);

        var readBuffer = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(readBuffer, ct)) > 0)
        {
            sha.TransformBlock(readBuffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static async Task ReplayAsync(HttpContext http, StoredResponse prior)
    {
        http.Response.StatusCode = prior.StatusCode;
        if (prior.ContentType is not null)
        {
            http.Response.ContentType = prior.ContentType;
        }

        // A marker so a client (and our own tests) can see a replay happened rather than a fresh run.
        http.Response.Headers["Idempotency-Replayed"] = "true";
        http.Response.ContentLength = prior.Body.Length;
        await http.Response.Body.WriteAsync(prior.Body, http.RequestAborted);
    }
}
