using Atlas.Domain.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");

        builder.HasKey(c => c.Code);

        // LEARN: the value object lives in the Domain; the fact that it's a CHAR(3)
        // column lives HERE. Neither knows about the other — the converter is the
        // border crossing, and it re-validates on the way in (CountryCode.From).
        builder.Property(c => c.Code)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
    }
}
