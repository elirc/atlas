using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();

        // Read-only scalar properties mapped explicitly (the S02 lesson; ModelMappingTests
        // fails on getter-only auto-properties EF would otherwise silently skip).
        builder.Property(q => q.OrgId);
        builder.Property(q => q.CreatedByUserId);
        builder.Property(q => q.CreatedAtUtc);

        builder.Property(q => q.Country)
            .HasConversion(code => code.Value, value => CountryCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength();

        // LEARN: a Money is a value object, so EF OWNS it (OwnsOne) rather than treating it
        // as a related entity. Table splitting puts Gross_Amount/Gross_Currency (etc.)
        // right on the quotes row — the value has no identity and no life without its owner.
        builder.OwnsOne(q => q.Gross, MapMoney<Quote>);
        builder.OwnsOne(q => q.TotalEmployerCost, MapMoney<Quote>);
        builder.OwnsOne(q => q.DisplayTotal, MapMoney<Quote>);

        // Lines: an owned COLLECTION — its own table, its own key, an FK back to the quote.
        // A quote line cannot exist apart from its quote, which is exactly what "owned" means.
        builder.OwnsMany(q => q.Lines, line =>
        {
            line.ToTable("quote_lines");
            line.WithOwner().HasForeignKey("QuoteId");

            line.HasKey(l => l.Id);
            line.Property(l => l.Id).ValueGeneratedNever();

            line.Property(l => l.Label).HasMaxLength(120).IsRequired();

            // WHY: store the enum as its NAME, not its int. A reordered enum must never
            // silently repoint historical rows — text survives refactors that ordinals don't.
            line.Property(l => l.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

            line.OwnsOne(l => l.Amount, MapMoney<QuoteLine>);
        });

        // WHY: the org-scoped newest-first walk (ADR-0002) orders by (OrgId, CreatedAtUtc, Id).
        builder.HasIndex(q => new { q.OrgId, q.CreatedAtUtc, q.Id });
    }

    // Shared owned-Money mapping, generic over the owner so the quote's three Money fields
    // and every line's amount are configured identically — one place, one rule.
    private static void MapMoney<TOwner>(OwnedNavigationBuilder<TOwner, Money> money)
        where TOwner : class
    {
        money.Property(m => m.Amount).HasPrecision(18, 2).IsRequired();

        money.Property(m => m.Currency)
            .HasConversion(code => code.Value, value => CurrencyCode.From(value))
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();
    }
}
