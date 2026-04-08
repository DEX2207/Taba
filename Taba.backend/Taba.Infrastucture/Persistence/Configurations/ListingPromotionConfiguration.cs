using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class ListingPromotionConfiguration : IEntityTypeConfiguration<ListingPromotion>
{
    public void Configure(EntityTypeBuilder<ListingPromotion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Tier).IsRequired().HasMaxLength(50);

        builder.HasOne(x => x.Listing)
            .WithMany()
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}