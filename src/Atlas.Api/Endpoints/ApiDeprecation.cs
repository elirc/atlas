namespace Atlas.Api.Endpoints;

/// <summary>
/// The deprecation contract for a v1 resource that has a v2 successor: a <c>Deprecation</c> date, a
/// <c>Sunset</c> date (RFC 8594), and a <c>Link</c> pointing at the successor and the policy doc.
/// Stamped on the v1 payslip response so a client learns — in-band, on every call — that it should
/// migrate, by when, and to what.
/// </summary>
// 📘 DEPRECATION IS A CONTRACT, NOT A BLOG POST (ADR-0015). A version does not vanish; it is
// announced deprecated, given a sunset date far enough out for integrators to move, and every
// response carries machine-readable pointers to its successor. The `Deprecation` header (the IETF
// draft) says "this is on the way out and has been since <date>"; `Sunset` (RFC 8594) says "it stops
// on <date>"; the `Link` rel="successor-version" says "go here instead." S10's webhook system is the
// OTHER half of the comms: a `payslip.v1_deprecated` announcement can fan out to registered
// integrators so the news does not depend on someone reading a response header (see the exercises).
internal static class ApiDeprecation
{
    /// <summary>When the v1 payslip resource was announced deprecated (HTTP-date, RFC 9110).</summary>
    public const string PayslipV1DeprecatedOn = "Wed, 15 Jul 2026 00:00:00 GMT";

    /// <summary>When the v1 payslip resource stops being served (HTTP-date, RFC 8594). 12 months out.</summary>
    public const string PayslipV1SunsetOn = "Thu, 15 Jul 2027 00:00:00 GMT";

    /// <summary>Stamp the v1 payslip deprecation headers, pointing at this payslip's v2 successor.</summary>
    public static void StampPayslipV1(HttpResponse response, Guid payslipId)
    {
        response.Headers["Deprecation"] = PayslipV1DeprecatedOn;
        response.Headers["Sunset"] = PayslipV1SunsetOn;
        response.Headers["Link"] =
            $"</api/v2/payslips/{payslipId}>; rel=\"successor-version\", " +
            "<https://atlas.example/docs/adr/ADR-0015>; rel=\"deprecation\"";
    }
}
