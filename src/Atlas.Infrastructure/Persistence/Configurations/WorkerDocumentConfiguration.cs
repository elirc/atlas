using Atlas.Domain.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class WorkerDocumentConfiguration : IEntityTypeConfiguration<WorkerDocument>
{
    public void Configure(EntityTypeBuilder<WorkerDocument> builder)
    {
        builder.ToTable("worker_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(d => d.Reference).HasMaxLength(400).IsRequired();

        builder.Property(d => d.OrgId);
        builder.Property(d => d.WorkerId);
        builder.Property(d => d.ExpiresOn);
        builder.Property(d => d.RecordedAtUtc);

        builder.HasIndex(d => new { d.WorkerId, d.OrgId });
        // The expiring-documents query: org + expiry, oldest-expiring first.
        builder.HasIndex(d => new { d.OrgId, d.ExpiresOn });
    }
}
