namespace Taba.Application.DTO;

public class CategoryFilterDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FilterType { get; set; } = string.Empty; 
    public List<string>? Options { get; set; }
    public int SortOrder { get; set; }
}