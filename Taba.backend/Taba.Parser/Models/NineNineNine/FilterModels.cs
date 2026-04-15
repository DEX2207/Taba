using System.Text.Json.Serialization;

namespace Taba.Parser.Models.NineNineNine;

public class GetFiltersResponse
{
    [JsonPropertyName("data")]
    public GetFiltersData? Data { get; set; }
}

public class GetFiltersData
{
    [JsonPropertyName("category")]
    public FilterCategory? Category { get; set; }
}

public class FilterCategory
{
    [JsonPropertyName("filters")]
    public List<NineFilter> Filters { get; set; } = new();
}

public class NineFilter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public Translation? Title { get; set; }

    [JsonPropertyName("units")]
    public List<string>? Units { get; set; }

    [JsonPropertyName("features")]
    public List<NineFilterFeature> Features { get; set; } = new();
}

public class NineFilterFeature
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<NineFilterOption> Options { get; set; } = new();
}

public class NineFilterOption
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public Translation? Title { get; set; }
}