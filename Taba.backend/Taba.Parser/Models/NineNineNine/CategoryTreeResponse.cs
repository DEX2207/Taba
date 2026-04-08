using System.Text.Json.Serialization;

namespace Taba.Parser.Models.NineNineNine;

public class CategoryTreeResponse
{
    [JsonPropertyName("data")]
    public CategoryTreeData? Data { get; set; }
}

public class CategoryTreeData
{
    [JsonPropertyName("categoryTree")]
    public NineCategory? CategoryTree { get; set; }
}

public class NineCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public Translation? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("categories")]
    public List<NineCategory> Categories { get; set; } = new();
}