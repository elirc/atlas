using Atlas.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Key).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Method).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Path).HasMaxLength(400).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(120);
        builder.Property(r => r.ResponseBody).IsRequired();

        builder.Property(r => r.OrgId);
        builder.Property(r => r.StatusCode);
        builder.Property(r => r.CreatedAtUtc);

        // The uniqueness that makes replay correct AND makes a concurrent double-submit collide
        // instead of double-execute: one response per (org, key).
        builder.HasIndex(r => new { r.OrgId, r.Key }).IsUnique();
    }
}
