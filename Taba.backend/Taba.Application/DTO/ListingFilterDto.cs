namespace Taba.Application.DTO;

public class ListingFilterDto
{
    public int? CategoryId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Search { get; set; }
    
    //Динамические фильтры
    public Dictionary<string, string> Attrs { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}