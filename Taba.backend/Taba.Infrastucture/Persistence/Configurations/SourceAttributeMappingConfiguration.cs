using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence.Configurations;

public class SourceAttributeMappingConfiguration : IEntityTypeConfiguration<SourceAttributeMapping>
{
    public void Configure(EntityTypeBuilder<SourceAttributeMapping> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RawKey)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.NormalizedKey)
            .HasMaxLength(100);

        builder.Property(x => x.Confidence)
            .HasColumnType("real");

        // Уникальность: один RawKey на источник + категорию
        // CategoryId входит в индекс — null и конкретный id считаются разными записями
        builder.HasIndex(x => new { x.SourceId, x.RawKey, x.CategoryId })
            .IsUnique();

        // Явно указываем HasForeignKey на обеих связях —
        // EF понимает что SourceId уже используется и не создаёт SourceId1
        builder.HasOne(x => x.Source)
            .WithMany(x=>x.AttributeMappings)
            .HasForeignKey(x => x.SourceId)
            .HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .HasPrincipalKey(x => x.Id)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}