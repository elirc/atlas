using System.Security.Cryptography;
using System.Text;

namespace Atlas.Infrastructure.Webhooks;

/// <summary>
/// Computes and verifies the <c>X-Atlas-Signature: t=&lt;unix&gt;,v1=&lt;hmac&gt;</c> header —
/// HMAC-SHA256 over <c>"{t}.{payload}"</c> keyed by the subscription's secret. The timestamp is
/// INSIDE the signed content on purpose: it lets a receiver reject stale (replayed) deliveries while
/// the HMAC proves the body wasn't tampered with.
/// </summary>
// 📘 The three defenses a receiver gets, and why each exists:
//   1. HMAC over the body → integrity + authenticity (only someone with the secret could compute it).
//   2. Timestamp in the signed payload → replay protection (reject t older than your tolerance), and
//      it can't be altered without breaking the HMAC.
//   3. CONSTANT-TIME comparison on verify → no timing side-channel that leaks the expected signature
//      byte by byte. A naive `==` on the hex string is the classic mistake; see IsValid below.
public static class WebhookSigner
{
    public const string HeaderName = "X-Atlas-Signature";

    public static string Sign(string secret, string payload, DateTimeOffset timestamp)
    {
        long t = timestamp.ToUnixTimeSeconds();
        string v1 = Hex(secret, $"{t}.{payload}");
        return $"t={t},v1={v1}";
    }

    /// <summary>
    /// Verify a header against the payload and secret, rejecting signatures older than
    /// <paramref name="tolerance"/>. Provided so the docs' "forge a body" exercise has a real target
    /// and so a future inbound-webhook receiver reuses exactly this logic.
    /// </summary>
    public static bool IsValid(
        string secret, string payload, string header, TimeSpan tolerance, DateTimeOffset nowUtc)
    {
        long? t = null;
        string? v1 = null;
        foreach (string part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal) && long.TryParse(part[2..], out long parsed))
            {
                t = parsed;
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                v1 = part[3..];
            }
        }

        if (t is not { } timestamp || v1 is null)
        {
            return false;
        }

        DateTimeOffset signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (nowUtc - signedAt > tolerance || signedAt - nowUtc > tolerance)
        {
            return false; // outside the tolerance window — a replay or a badly-skewed clock
        }

        string expected = Hex(secret, $"{timestamp}.{payload}");
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
    }

    private static string Hex(string secret, string signedContent) =>
        Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedContent)))
            .ToLowerInvariant();
}
