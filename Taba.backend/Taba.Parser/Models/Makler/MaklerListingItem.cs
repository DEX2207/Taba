namespace Taba.Parser.Models.Makler;

public class MaklerListingItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "MDL";
    public string City { get; set; } = string.Empty;
    public string DateRaw { get; set; } = string.Empty;
    public string? PreviewImageUrl { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsTop { get; set; }
}