namespace Taba.Domain.Entities;

public class ListingImage
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    public Listing Listing { get; set; } = null!;
}