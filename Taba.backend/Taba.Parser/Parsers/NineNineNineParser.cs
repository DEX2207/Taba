using System.Text.Json;
using Taba.Parser.Models.NineNineNine;

namespace Taba.Parser.Parsers;

public class NineNineNineParser
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NineNineNineParser> _logger;

    private const string GraphQlUrl = "https://999.md/graphql";
    private const string ImageBaseUrl = "https://i.simpalsmedia.com/999.md/BoardImages/900x900/";
    private const string AdBaseUrl = "https://999.md/ru/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NineNineNineParser(HttpClient httpClient, ILogger<NineNineNineParser> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<Ad>> FetchAdsAsync(int subCategoryId, int limit = 78, int skip = 0)
    {
    var query = "query SearchAds($input: Ads_SearchInput!, $isWorkCategory: Boolean = false, $includeOwner: Boolean = false, $locale: Common_Locale) {\n  searchAds(input: $input) {\n    ads {\n      ...AdsSearchFragment\n      __typename\n    }\n    count\n    reseted\n    __typename\n  }\n}\n\nfragment AdsSearchFragment on Advert {\n  ...AdListFragment\n  ...WorkCategoryFeatures @include(if: $isWorkCategory)\n  reseted(\n    input: {format: \"2 Jan. 2006, 15:04\", locale: $locale, timezone: \"Europe/Chisinau\", getDiff: false}\n  )\n  __typename\n}\n\nfragment AdListFragment on Advert {\n  id\n  title\n  subCategory {\n    ...CategoryAdFragment\n    __typename\n  }\n  ...PriceAndImages\n  ...AdvertOwner @include(if: $includeOwner)\n  __typename\n}\n\nfragment CategoryAdFragment on Category {\n  id\n  title {\n    ...TranslationFragment\n    __typename\n  }\n  parent {\n    id\n    title {\n      ...TranslationFragment\n      __typename\n    }\n    parent {\n      id\n      title {\n        ...TranslationFragment\n        __typename\n      }\n      __typename\n    }\n    __typename\n  }\n  __typename\n}\n\nfragment TranslationFragment on I18NTr {\n  translated\n  __typename\n}\n\nfragment PriceAndImages on Advert {\n  price: feature(id: 2) {\n    ...FeatureValueFragment\n    __typename\n  }\n  images: feature(id: 14) {\n    ...FeatureValueFragment\n    __typename\n  }\n  __typename\n}\n\nfragment FeatureValueFragment on FeatureValue {\n  id\n  type\n  value\n  __typename\n}\n\nfragment AdvertOwner on Advert {\n  owner {\n    ...AccountFragment\n    __typename\n  }\n  __typename\n}\n\nfragment AccountFragment on Account {\n  id\n  login\n  avatar\n  createdDate\n  __typename\n}\n\nfragment WorkCategoryFeatures on Advert {\n  salary: feature(id: 266) {\n    ...FeatureValueFragment\n    __typename\n  }\n  __typename\n}";

    var payload = new
    {
        operationName = "SearchAds",
        query,
        variables = new
        {
            isWorkCategory = false,
            includeOwner = true,
            locale = "ru_RU",
            input = new
            {
                source = "AD_SOURCE_DESKTOP_REDESIGN",
                sort = "SORT_ADS_DATE_DESC",
                subCategoryId,
                pagination = new { limit, skip }
            }
        }
    };

    var json = JsonSerializer.Serialize(payload);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    var response = await _httpClient.PostAsync(GraphQlUrl, content);
    var responseJson = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogError("Ошибка {StatusCode}: {Body}", (int)response.StatusCode, responseJson);
        return new List<Ad>();
    }

    var result = JsonSerializer.Deserialize<SearchAdsResponse>(responseJson, JsonOptions);
    return result?.Data?.SearchAds?.Ads ?? new List<Ad>();
    }

    public string BuildAdUrl(string externalId) => AdBaseUrl + externalId;

    public List<string> BuildImageUrls(Ad ad)
    {
        if (ad.Images?.Value is not { } value)
            return new List<string>();

        try
        {
            var filenames = value.Deserialize<List<string>>(JsonOptions);
            return filenames?.Select(f => ImageBaseUrl + f).ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public (decimal Price, string Currency) ExtractPrice(Ad ad)
    {
        if (ad.Price?.Value is not { } value)
            return (0, "MDL");

        try
        {
            var priceValue = value.Deserialize<PriceValue>(JsonOptions);
            var currency = priceValue?.Unit?.Replace("UNIT_", "") ?? "MDL";
            return (priceValue?.Value ?? 0, currency);
        }
        catch
        {
            return (0, "MDL");
        }
    }
    public async Task<NineCategory?> FetchCategoryTreeAsync()
    {
        var query = "query CategoryTreeMinimal($input: GetCategoryTreeRequestInput!) {\n  categoryTree(input: $input) {\n    ...CategoryTreeMinimalF\n    __typename\n  }\n}\n\nfragment CategoryTreeMinimalF on Category {\n  id\n  title {\n    translated\n    __typename\n  }\n  url\n  type\n  categories {\n    id\n    title {\n      translated\n      __typename\n    }\n    url\n    type\n    categories {\n      id\n      title {\n        translated\n        __typename\n      }\n      url\n      type\n      categories {\n        id\n        title {\n          translated\n          __typename\n        }\n        url\n        type\n        __typename\n      }\n      __typename\n    }\n    __typename\n  }\n  __typename\n}";

        var payload = new
        {
            operationName = "CategoryTreeMinimal",
            query,
            variables = new { input = new { id = 0 } }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(GraphQlUrl, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ошибка получения категорий {StatusCode}: {Body}",
                (int)response.StatusCode, responseJson);
            return null;
        }

        var result = JsonSerializer.Deserialize<CategoryTreeResponse>(responseJson, JsonOptions);
        return result?.Data?.CategoryTree;
    }

    // Вспомогательный метод — собирает все конечные подкатегории (листья дерева)
    public List<NineCategory> GetLeafCategories(NineCategory root)
    {
        var leaves = new List<NineCategory>();
        CollectLeaves(root, leaves);
        return leaves;
    }

    private void CollectLeaves(NineCategory category, List<NineCategory> leaves)
    {
        if (category.Categories.Count == 0)
        {
            leaves.Add(category);
            return;
        }
        foreach (var child in category.Categories)
            CollectLeaves(child, leaves);
    }
    public async Task<Dictionary<int, System.Text.Json.JsonElement>> FetchAdDetailsAsync(string adId)
    {
        var featureIds = new[] { 7, 13, 19, 20, 21, 101, 102, 103, 104, 107, 108, 151 };

        var fields = string.Join(" ", featureIds.Select(id =>
            $"f{id}: feature(id: {id}) {{ id type value }}"));

        var query = $"query GetAd($input: AdvertInput!) {{ advert(input: $input) {{ id {fields} }} }}";

        var payload = new
        {
            operationName = "GetAd",
            query,
            variables = new { input = new { id = adId } }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(GraphQlUrl, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ошибка деталей объявления {Id}: {Status}", adId, response.StatusCode);
            return new Dictionary<int, System.Text.Json.JsonElement>();
        }

        var result = new Dictionary<int, System.Text.Json.JsonElement>();

        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
        if (!data.TryGetProperty("advert", out var advert)) return result;
        if (advert.ValueKind == System.Text.Json.JsonValueKind.Null) return result;

        foreach (var featureId in featureIds)
        {
            if (advert.TryGetProperty($"f{featureId}", out var feature) &&
                feature.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                result[featureId] = feature.Clone();
            }
        }

        return result;
    }
    public string? ExtractFeatureValue(System.Text.Json.JsonElement feature)
    {
        try
        {
            if (!feature.TryGetProperty("value", out var value)) return null;

            // FEATURE_OPTIONS — у него поле "translated"
            if (value.TryGetProperty("translated", out var translated))
                return translated.GetString();

            // FEATURE_INT_UNIT — у него поле "value"
            if (value.TryGetProperty("value", out var intVal))
                return intVal.ToString();

            return value.ToString();
        }
        catch
        {
            return null;
        }
    }
    public async Task<List<NineFilter>> FetchCategoryFiltersAsync(int categoryId)
    {
        var query = "query GetFilters($input: GetCategoryRequestInput!) {\n  category(input: $input) {\n    filters {\n      id\n      type\n      title { translated }\n      units\n      features {\n        id\n        type\n        options {\n          id\n          title { translated }\n          __typename\n        }\n        __typename\n      }\n      __typename\n    }\n    __typename\n  }\n}";

        var payload = new
        {
            operationName = "GetFilters",
            query,
            variables = new { input = new { id = categoryId, values = Array.Empty<object>() } }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(GraphQlUrl, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ошибка GetFilters для категории {Id}: {Status}", categoryId, response.StatusCode);
            return new List<NineFilter>();
        }

        var result = JsonSerializer.Deserialize<GetFiltersResponse>(responseJson, JsonOptions);
        return result?.Data?.Category?.Filters ?? new List<NineFilter>();
    }
}