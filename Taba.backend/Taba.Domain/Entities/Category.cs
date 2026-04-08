namespace Taba.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<SourceCategoryMapping> SourceMappings { get; set; } = new List<SourceCategoryMapping>();
}