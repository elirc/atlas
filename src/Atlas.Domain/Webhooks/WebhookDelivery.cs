namespace Atlas.Domain.Webhooks;

public enum WebhookDeliveryStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3,
}

/// <summary>
/// The durable record of ONE attempt-track to deliver ONE event to ONE subscription. This is the
/// per-endpoint delivery log the outbox fans out into: the outbox guarantees these rows get created;
/// the row then tracks whether that specific partner endpoint accepted the payload, and lets a human
/// (or a retry) redeliver a failed one.
/// </summary>
public sealed class WebhookDelivery
{
    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string EventType { get; private set; }
    public string PayloadJson { get; private set; }
    public WebhookDeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    private WebhookDelivery(
        Guid orgId, Guid subscriptionId, string eventType, string payloadJson, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        OrgId = orgId;
        SubscriptionId = subscriptionId;
        EventType = eventType;
        PayloadJson = payloadJson;
        Status = WebhookDeliveryStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public static WebhookDelivery Queue(
        Guid orgId, Guid subscriptionId, string eventType, string payloadJson, DateTimeOffset nowUtc) =>
        new(orgId, subscriptionId, eventType, payloadJson, nowUtc);

    public void RecordSuccess(int responseStatusCode, DateTimeOffset nowUtc)
    {
        Attempts++;
        Status = WebhookDeliveryStatus.Delivered;
        ResponseStatusCode = responseStatusCode;
        LastError = null;
        DeliveredAtUtc = nowUtc;
    }

    public void RecordFailure(string error, int? responseStatusCode, DateTimeOffset nowUtc)
    {
        Attempts++;
        Status = WebhookDeliveryStatus.Failed;
        ResponseStatusCode = responseStatusCode;
        LastError = error;
    }

    // EF materialization only.
    private WebhookDelivery()
    {
        EventType = null!;
        PayloadJson = null!;
    }
}
