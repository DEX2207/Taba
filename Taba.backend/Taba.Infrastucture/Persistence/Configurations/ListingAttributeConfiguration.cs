using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class ListingAttributeConfiguration : IEntityTypeConfiguration<ListingAttribute>
{
    public void Configure(EntityTypeBuilder<ListingAttribute> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Value).IsRequired().HasMaxLength(500);

        builder.HasIndex(x => new { x.ListingId, x.Key });

        builder.HasOne(x => x.Listing)
            .WithMany(x => x.Attributes)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}