namespace Taba.Domain.Entities;

public class SourceCountry
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public int CountryId { get; set; }

    public Source Source { get; set; } = null!;
    public Country Country { get; set; } = null!;
}