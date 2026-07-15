using System.Globalization;
using Atlas.Api.Idempotency;
using Atlas.Application.Errors;
using Atlas.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Errors;

/// <summary>
/// Translates the application's failure vocabulary into RFC 7807 responses.
/// One translation table, applied everywhere — no endpoint does its own mapping.
/// </summary>
public sealed class AtlasExceptionHandler(IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // LEARN: the failure families and their contracts —
        //   404 not-found:            the resource isn't there (or you may not know)
        //   400 invalid-request:      the request itself is malformed
        //   422 domain-rule-violated: well-formed request, but the business said no
        //   401 / 403:                who you are / what you may do
        //   429 too-many-requests:    you're rate-limited — back off (Retry-After)
        //   503 vendor-unavailable:   a downstream we depend on is momentarily down — retry
        // Anything else is a genuine bug and stays a 500 (handled by the default pipeline; we
        // deliberately do NOT catch it here). UnmappedVendorStateException is intentionally in
        // that "anything else" bucket — an unmapped vendor state is OUR bug, and it SHOULD be a
        // loud 500 that pages someone, not a smoothed-over 4xx. (ADR-0010.)
        (int status, string type, string title) = exception switch
        {
            NotFoundException => (
                StatusCodes.Status404NotFound,
                AtlasProblems.NotFound,
                "Resource not found"),
            RequestValidationException => (
                StatusCodes.Status400BadRequest,
                AtlasProblems.InvalidRequest,
                "Invalid request"),
            DomainRuleException => (
                StatusCodes.Status422UnprocessableEntity,
                AtlasProblems.DomainRuleViolated,
                "Business rule violated"),
            // S10: same idempotency key, different request body. Well-formed, but semantically a
            // contradiction the client must fix — a 422 with its OWN type so integrations can tell it
            // apart from a business-rule 422 (retry-with-a-fresh-key vs fix-the-request).
            IdempotencyConflictException => (
                StatusCodes.Status422UnprocessableEntity,
                AtlasProblems.IdempotencyConflict,
                "Idempotency key conflict"),
            UnauthenticatedException => (
                StatusCodes.Status401Unauthorized,
                AtlasProblems.Unauthenticated,
                "Authentication required"),
            ForbiddenException => (
                StatusCodes.Status403Forbidden,
                AtlasProblems.Forbidden,
                "Permission denied"),
            // S13: brute-force throttling. A retryable 429 — the client should wait for the
            // Retry-After window rather than hammering into a longer lockout.
            TooManyRequestsException => (
                StatusCodes.Status429TooManyRequests,
                AtlasProblems.TooManyRequests,
                "Too many requests"),
            // WHY a distinct 503 (not a 500): the request is fine and the business rules are
            // satisfied — a downstream vendor is just momentarily unreachable. Telling the client
            // "retry" (503) rather than "you broke something" (500 or 4xx) is the honest signal.
            PaymentProviderUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                AtlasProblems.VendorUnavailable,
                "Payment provider unavailable"),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        if (status == StatusCodes.Status401Unauthorized)
        {
            // WHY: RFC 7235 — a 401 MUST say how to authenticate.
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        }

        if (exception is TooManyRequestsException throttled)
        {
            // WHY: RFC 6585 — a 429 SHOULD carry Retry-After (whole seconds) so a well-behaved
            // client waits the cool-down instead of extending its own lockout.
            int seconds = Math.Max(1, (int)Math.Ceiling(throttled.RetryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Type = type,
                Title = title,
                // WHY: exception messages here are written for API consumers (they
                // come from our own validation), so exposing them is deliberate.
                // A 500's message is NOT safe the same way — which is one more
                // reason unknown exceptions don't take this path.
                Detail = exception.Message,
            },
        });
    }
}
