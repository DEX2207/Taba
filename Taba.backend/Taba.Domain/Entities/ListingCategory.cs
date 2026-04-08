using Taba.Domain.Enums;

namespace Taba.Domain.Entities;

public class ListingCategory
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public int CategoryId { get; set; }
    public float? Confidence { get; set; }
    public ListingCategorySource Source { get; set; }

    public Listing Listing { get; set; } = null!;
    public Category Category { get; set; } = null!;
}