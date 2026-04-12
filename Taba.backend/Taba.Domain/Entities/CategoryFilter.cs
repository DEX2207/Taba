using System.ComponentModel.DataAnnotations.Schema;

namespace Taba.Domain.Entities;

public class CategoryFilter
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int? SourceFeatureId { get; set; } 
    public string Key { get; set; } = string.Empty;      
    public string Label { get; set; } = string.Empty;     
    public string FilterType { get; set; } = string.Empty; 
    public string? Options { get; set; }                  
    public bool IsInherited { get; set; } = false;
    public int SortOrder { get; set; } = 0;
    
    public Category Category { get; set; } = null!;
}