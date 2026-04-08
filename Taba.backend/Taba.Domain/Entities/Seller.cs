namespace Taba.Domain.Entities;

public class Seller
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalSellerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public Source Source { get; set; } = null!;
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}