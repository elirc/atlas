using Atlas.Domain;
using Atlas.Domain.Webhooks;
using Xunit;

namespace Atlas.Domain.Tests.Webhooks;

public class WebhookSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static WebhookSubscription Create(string url, params string[] events) =>
        WebhookSubscription.Create(Guid.NewGuid(), url, "whsec_x", events.Length == 0 ? null : events, Now);

    [Fact]
    public void An_http_url_is_rejected()
    {
        // Plaintext defeats the signature's purpose — an on-path attacker could read AND replay.
        Assert.Throws<DomainRuleException>(() => Create("http://partner.example/hook"));
    }

    [Fact]
    public void A_non_absolute_url_is_rejected()
    {
        Assert.Throws<DomainRuleException>(() => Create("/hook"));
    }

    [Fact]
    public void An_empty_filter_means_all_events()
    {
        WebhookSubscription sub = Create("https://partner.example/hook");

        Assert.Equal(WebhookSubscription.AllEvents, sub.EventFilter);
        Assert.True(sub.WantsEvent("invoice.paid"));
        Assert.True(sub.WantsEvent("anything.at.all"));
    }

    [Fact]
    public void A_filter_only_matches_its_listed_types()
    {
        WebhookSubscription sub = Create("https://partner.example/hook", "invoice.paid", "invoice.payment_failed");

        Assert.True(sub.WantsEvent("invoice.paid"));
        Assert.True(sub.WantsEvent("INVOICE.PAID")); // case-insensitive
        Assert.False(sub.WantsEvent("engagement.activated"));
    }

    [Fact]
    public void A_paused_subscription_wants_nothing_and_pause_is_idempotent()
    {
        WebhookSubscription sub = Create("https://partner.example/hook");
        Assert.True(sub.WantsEvent("invoice.paid"));

        sub.Pause(Now);
        DateTimeOffset pausedAt = sub.PausedAtUtc!.Value;
        Assert.False(sub.IsActive);
        Assert.False(sub.WantsEvent("invoice.paid"));

        sub.Pause(Now.AddHours(1)); // second pause
        Assert.Equal(pausedAt, sub.PausedAtUtc); // unchanged
    }
}
