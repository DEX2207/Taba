using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Url).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.RawRegionName).HasMaxLength(200);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);

        builder.HasOne(x => x.Source)
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Region)
            .WithMany()
            .HasForeignKey(x => x.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Seller)
            .WithMany(x => x.Listings)
            .HasForeignKey(x => x.SellerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Images)
            .WithOne(x => x.Listing)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Categories)
            .WithOne(x => x.Listing)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.PriceHistory)
            .WithOne(x => x.Listing)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Favorites)
            .WithOne(x => x.Listing)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}