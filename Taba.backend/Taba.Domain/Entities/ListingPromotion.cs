namespace Taba.Domain.Entities;

public class ListingPromotion
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public int UserId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Tier { get; set; } = string.Empty;

    public Listing Listing { get; set; } = null!;
    public User User { get; set; } = null!;
}