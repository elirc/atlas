using Atlas.Domain.Orgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class ClientOrgConfiguration : IEntityTypeConfiguration<ClientOrg>
{
    public void Configure(EntityTypeBuilder<ClientOrg> builder)
    {
        builder.ToTable("client_orgs");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Name)
            .HasMaxLength(ClientOrg.MaxNameLength)
            .IsRequired();

        builder.Property(o => o.CreatedAtUtc).IsRequired();

        // WHY: the newest-first cursor walk (ADR-0002) orders by (CreatedAtUtc, Id) —
        // this index IS that query. Pagination without its index is a slow table scan
        // wearing a fast API.
        builder.HasIndex(o => new { o.CreatedAtUtc, o.Id });
    }
}
