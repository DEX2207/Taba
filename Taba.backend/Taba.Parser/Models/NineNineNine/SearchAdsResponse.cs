using System.Text.Json.Serialization;

namespace Taba.Parser.Models.NineNineNine;

public class SearchAdsResponse
{
    [JsonPropertyName("data")]
    public SearchAdsData? Data { get; set; }
}

public class SearchAdsData
{
    [JsonPropertyName("searchAds")]
    public SearchAdsResult? SearchAds { get; set; }
}

public class SearchAdsResult
{
    [JsonPropertyName("ads")]
    public List<Ad> Ads { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class Ad
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("price")]
    public FeatureValue? Price { get; set; }

    [JsonPropertyName("images")]
    public FeatureValue? Images { get; set; }

    [JsonPropertyName("subCategory")]
    public AdCategory? SubCategory { get; set; }

    [JsonPropertyName("reseted")]
    public string? Reseted { get; set; }

    [JsonPropertyName("owner")]
    public AdOwner? Owner { get; set; }
}

public class FeatureValue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement? Value { get; set; }
}

public class AdCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public Translation? Title { get; set; }

    [JsonPropertyName("parent")]
    public AdCategory? Parent { get; set; }
}

public class Translation
{
    [JsonPropertyName("translated")]
    public string? Translated { get; set; }
}

public class AdOwner
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

public class PriceValue
{
    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}