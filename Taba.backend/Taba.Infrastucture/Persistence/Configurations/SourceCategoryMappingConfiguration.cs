using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class SourceCategoryMappingConfiguration : IEntityTypeConfiguration<SourceCategoryMapping>
{
    public void Configure(EntityTypeBuilder<SourceCategoryMapping> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalCategoryName).IsRequired().HasMaxLength(200);

        builder.HasIndex(x => new { x.SourceId, x.ExternalCategoryName }).IsUnique();

        builder.HasOne(x => x.Source)
            .WithMany(x => x.CategoryMappings)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.SourceMappings)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}