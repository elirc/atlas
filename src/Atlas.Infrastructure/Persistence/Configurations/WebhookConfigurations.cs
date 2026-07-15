using Atlas.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("webhook_subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Url).HasMaxLength(2000).IsRequired();
        builder.Property(s => s.Secret).HasMaxLength(200).IsRequired();
        builder.Property(s => s.EventFilter).HasMaxLength(2000).IsRequired();

        builder.Property(s => s.OrgId);
        builder.Property(s => s.IsActive);
        builder.Property(s => s.CreatedAtUtc);
        builder.Property(s => s.PausedAtUtc);

        builder.HasIndex(s => new { s.OrgId, s.IsActive });
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.EventType).HasMaxLength(200).IsRequired();
        builder.Property(d => d.PayloadJson).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(2000);

        builder.Property(d => d.OrgId);
        builder.Property(d => d.SubscriptionId);
        builder.Property(d => d.Attempts);
        builder.Property(d => d.ResponseStatusCode);
        builder.Property(d => d.CreatedAtUtc);
        builder.Property(d => d.DeliveredAtUtc);

        builder.HasIndex(d => new { d.OrgId, d.CreatedAtUtc, d.Id });
        builder.HasIndex(d => d.SubscriptionId);
    }
}
