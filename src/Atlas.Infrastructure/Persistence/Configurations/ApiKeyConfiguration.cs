using Atlas.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();

        builder.Property(k => k.Name).HasMaxLength(120).IsRequired();
        builder.Property(k => k.DisplayPrefix).HasMaxLength(20).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(k => k.ScopeCsv).HasMaxLength(1000).IsRequired();

        builder.Property(k => k.OrgId);
        builder.Property(k => k.CreatedAtUtc);
        builder.Property(k => k.RevokedAtUtc);

        // IsActive is computed from RevokedAtUtc — not a column (ModelMappingTests allowlists it).
        builder.Ignore(k => k.IsActive);

        // The auth hot path: resolve a presented key by its hash. Unique — one key, one row.
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.OrgId);
    }
}
