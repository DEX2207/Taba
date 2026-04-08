namespace Taba.Domain.Entities;

public class SourceCategoryMapping
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalCategoryName { get; set; } = string.Empty;
    public int CategoryId { get; set; }

    public Source Source { get; set; } = null!;
    public Category Category { get; set; } = null!;
}