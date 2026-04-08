namespace Taba.Domain.Entities;

public class Source
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public ICollection<SourceCountry> SourceCountries { get; set; } = new List<SourceCountry>();
    public ICollection<SourceCategoryMapping> CategoryMappings { get; set; } = new List<SourceCategoryMapping>();
}