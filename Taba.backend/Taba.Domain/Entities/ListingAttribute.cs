namespace Taba.Domain.Entities;

public class ListingAttribute
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public Listing Listing { get; set; } = null!;
}