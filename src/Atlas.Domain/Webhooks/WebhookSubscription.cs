namespace Atlas.Domain.Webhooks;

/// <summary>
/// A partner's standing request to be told when things happen in their org: an HTTPS endpoint, a
/// signing secret, and a filter over which event types to receive. The secret is stored because we
/// must SIGN outbound deliveries with it (unlike a session token, which we only ever verify) — a
/// deliberate asymmetry called out at the boundary.
/// </summary>
// ⚠️ Storing a signing secret in readable form is a real exposure — a leaked table lets an attacker
// forge deliveries. Real systems encrypt these at rest (envelope encryption / KMS). Atlas keeps it
// plaintext for the course and records the debt: see ADR-0011 and the S13 threat-model backlog. The
// point is to name the tradeoff, not to pretend it away.
public sealed class WebhookSubscription
{
    public const string AllEvents = "*";

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Url { get; private set; }
    public string Secret { get; private set; }

    /// <summary>CSV of event types to receive, or <see cref="AllEvents"/> for every event.</summary>
    public string EventFilter { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }

    private WebhookSubscription(
        Guid orgId, string url, string secret, string eventFilter, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        OrgId = orgId;
        Url = url;
        Secret = secret;
        EventFilter = eventFilter;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public static WebhookSubscription Create(
        Guid orgId, string? url, string secret, IReadOnlyList<string>? eventTypes, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            // HTTPS only: a plaintext webhook endpoint leaks the payload and defeats the signature's
            // whole purpose (an attacker on the wire could read AND replay).
            throw new DomainRuleException("A webhook URL must be an absolute https:// endpoint.");
        }

        string filter = eventTypes is null or { Count: 0 }
            ? AllEvents
            : string.Join(',', eventTypes.Select(NormalizeType));

        return new WebhookSubscription(orgId, parsed.ToString(), secret, filter, nowUtc);
    }

    /// <summary>Does this subscription want the given event type?</summary>
    public bool WantsEvent(string eventType)
    {
        if (!IsActive)
        {
            return false;
        }

        if (EventFilter == AllEvents)
        {
            return true;
        }

        string wanted = NormalizeType(eventType);
        return EventFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t == wanted);
    }

    public void Pause(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return; // idempotent — pausing a paused subscription is a no-op, not an error
        }

        IsActive = false;
        PausedAtUtc = nowUtc;
    }

    private static string NormalizeType(string type) => type.Trim().ToLowerInvariant();

    // EF materialization only.
    private WebhookSubscription()
    {
        Url = null!;
        Secret = null!;
        EventFilter = null!;
    }
}
