using System.Net.Http.Headers;
using System.Text;
using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Webhooks;

/// <summary>
/// The real network adapter for <see cref="IWebhookTransport"/>: POST the signed JSON to the
/// partner's URL over a time-boxed HttpClient. A non-2xx or a transport error becomes a failure
/// RESULT, not an exception — the caller (the fan-out) records it on the delivery row and moves on.
/// </summary>
internal sealed class HttpWebhookTransport(IHttpClientFactory httpClientFactory, TimeProvider clock)
    : IWebhookTransport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<WebhookSendResult> SendAsync(
        string url, string payloadJson, string secret, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient("webhooks");
        client.Timeout = Timeout;

        string signatureHeader = WebhookSigner.Sign(secret, payloadJson, clock.GetUtcNow());

        using var content = new StringContent(payloadJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add(WebhookSigner.HeaderName, signatureHeader);

        try
        {
            using HttpResponseMessage response = await client.PostAsync(url, content, cancellationToken);
            return response.IsSuccessStatusCode
                ? WebhookSendResult.Ok((int)response.StatusCode)
                : WebhookSendResult.Fail($"partner returned {(int)response.StatusCode}", (int)response.StatusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebhookSendResult.Fail("delivery timed out");
        }
        catch (HttpRequestException ex)
        {
            return WebhookSendResult.Fail(ex.Message);
        }
    }
}
