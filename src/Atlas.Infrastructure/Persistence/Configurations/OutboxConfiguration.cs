using Atlas.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
        builder.Property(m => m.SubjectType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.PayloadJson).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Read-only mapped properties (ModelMappingTests enforces every one is here).
        builder.Property(m => m.OrgId);
        builder.Property(m => m.SubjectId);
        builder.Property(m => m.OccurredAtUtc);
        builder.Property(m => m.Attempts);
        builder.Property(m => m.NextAttemptAtUtc);
        builder.Property(m => m.DispatchedAtUtc);

        // The dispatcher's hot query: undelivered rows that are due, oldest first. A filtered index
        // on (DispatchedAtUtc, NextAttemptAtUtc) keeps the drain O(backlog), not O(table).
        builder.HasIndex(m => new { m.DispatchedAtUtc, m.NextAttemptAtUtc });
    }
}
