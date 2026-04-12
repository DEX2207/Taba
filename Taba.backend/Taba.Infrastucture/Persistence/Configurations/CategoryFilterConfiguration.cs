using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class CategoryFilterConfiguration : IEntityTypeConfiguration<CategoryFilter>
{
    public void Configure(EntityTypeBuilder<CategoryFilter> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(200);
        builder.Property(x => x.FilterType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Options).HasColumnType("text");

        builder.HasIndex(x => x.CategoryId);
        builder.HasIndex(x => new { x.CategoryId, x.Key });
        
        builder.Property(x => x.SourceFeatureId).IsRequired(false);
        builder.HasIndex(x => new { x.CategoryId, x.SourceFeatureId });

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Filters)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}