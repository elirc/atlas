using Atlas.Infrastructure.Webhooks;
using Xunit;

namespace Atlas.Api.Tests;

/// <summary>
/// The executable answer to the "forge a webhook body" exercise: every defense named in the signer,
/// proven. Sign once, then try to make a lie pass.
/// </summary>
public class WebhookSignerTests
{
    private const string Secret = "whsec_test_secret";
    private const string Payload = """{"type":"invoice.paid","data":{"amount":100}}""";
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact]
    public void A_freshly_signed_payload_verifies()
    {
        string header = WebhookSigner.Sign(Secret, Payload, Now);

        Assert.True(WebhookSigner.IsValid(Secret, Payload, header, Tolerance, Now));
        Assert.StartsWith("t=", header);
        Assert.Contains(",v1=", header);
    }

    [Fact]
    public void A_tampered_body_fails()
    {
        string header = WebhookSigner.Sign(Secret, Payload, Now);
        string forged = Payload.Replace("100", "1000000");

        Assert.False(WebhookSigner.IsValid(Secret, forged, header, Tolerance, Now));
    }

    [Fact]
    public void The_wrong_secret_fails()
    {
        string header = WebhookSigner.Sign(Secret, Payload, Now);

        Assert.False(WebhookSigner.IsValid("whsec_not_the_secret", Payload, header, Tolerance, Now));
    }

    [Fact]
    public void A_stale_signature_is_rejected_even_though_the_hmac_is_valid()
    {
        // Replay protection: the HMAC is genuine, but the timestamp is outside tolerance.
        string header = WebhookSigner.Sign(Secret, Payload, Now);

        Assert.False(WebhookSigner.IsValid(Secret, Payload, header, Tolerance, Now.AddMinutes(10)));
    }

    [Fact]
    public void A_garbage_header_fails_closed()
    {
        Assert.False(WebhookSigner.IsValid(Secret, Payload, "not-a-signature", Tolerance, Now));
        Assert.False(WebhookSigner.IsValid(Secret, Payload, "t=abc,v1=xyz", Tolerance, Now));
    }
}
