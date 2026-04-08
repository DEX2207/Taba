using Taba.Domain.Enums;

namespace Taba.Domain.Entities;

public class Listing
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public ListingStatus Status { get; set; } = ListingStatus.Active;
    public int CountryId { get; set; }
    public int? RegionId { get; set; }
    public string? RawRegionName { get; set; }
    public int? SellerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ParsedAt { get; set; }

    public Source Source { get; set; } = null!;
    public Country Country { get; set; } = null!;
    public Region? Region { get; set; }
    public Seller? Seller { get; set; }
    public ICollection<ListingImage> Images { get; set; } = new List<ListingImage>();
    public ICollection<ListingCategory> Categories { get; set; } = new List<ListingCategory>();
    public ICollection<ListingPriceHistory> PriceHistory { get; set; } = new List<ListingPriceHistory>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<ListingAttribute> Attributes { get; set; } = new List<ListingAttribute>();
}