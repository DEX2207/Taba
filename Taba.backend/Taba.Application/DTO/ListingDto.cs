namespace Taba.Application.DTO;

public class ListingDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? RegionName { get; set; }
    public List<string> Images { get; set; } = new();
    public Dictionary<string, string> Attributes { get; set; } = new();
}