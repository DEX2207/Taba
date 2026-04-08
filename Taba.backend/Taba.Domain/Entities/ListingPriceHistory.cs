namespace Taba.Domain.Entities;

public class ListingPriceHistory
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }

    public Listing Listing { get; set; } = null!;
}