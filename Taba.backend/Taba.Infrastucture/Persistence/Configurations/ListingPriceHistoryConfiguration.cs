using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class ListingPriceHistoryConfiguration : IEntityTypeConfiguration<ListingPriceHistory>
{
    public void Configure(EntityTypeBuilder<ListingPriceHistory> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(50);

        builder.HasIndex(x => x.RecordedAt);
    }
}
