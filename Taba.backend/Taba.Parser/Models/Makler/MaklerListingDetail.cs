namespace Taba.Parser.Models.Makler;

public class MaklerListingDetail
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "MDL";
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Description { get; set; }
    public string DateRaw { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = new();
    public string? Phone { get; set; }
    public string? SellerName { get; set; }
    public string? ExternalSellerId { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
}