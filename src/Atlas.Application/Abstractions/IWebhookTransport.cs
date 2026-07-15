namespace Atlas.Application.Abstractions;

/// <summary>The outcome of one HTTP delivery attempt to a partner endpoint.</summary>
public sealed record WebhookSendResult(bool Success, int? StatusCode, string? Error)
{
    public static WebhookSendResult Ok(int statusCode) => new(true, statusCode, null);

    public static WebhookSendResult Fail(string error, int? statusCode = null) => new(false, statusCode, error);
}

/// <summary>
/// Signs and sends a webhook payload to a partner URL. A PORT because the real adapter does network
/// I/O (an HttpClient) AND the HMAC signing — both of which the fan-out logic and its tests must stay
/// free of. A fake transport lets the delivery be tested without a live endpoint, and lets us
/// simulate a partner returning 500 or timing out. The adapter owns the signature format so callers
/// (fan-out and redelivery alike) never touch HMAC.
/// </summary>
public interface IWebhookTransport
{
    Task<WebhookSendResult> SendAsync(
        string url, string payloadJson, string secret, CancellationToken cancellationToken);
}
