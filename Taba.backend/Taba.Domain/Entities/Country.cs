namespace Taba.Domain.Entities;

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Region> Regions { get; set; } = new List<Region>();
    public ICollection<SourceCountry> SourceCountries { get; set; } = new List<SourceCountry>();
}