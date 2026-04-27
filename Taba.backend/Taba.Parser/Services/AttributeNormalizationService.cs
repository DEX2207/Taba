using Microsoft.EntityFrameworkCore;
using Taba.Domain.Entities;
using Taba.Infrastucture.Persistence;

namespace Taba.Parser.Services;

/// <summary>
/// Сервис нормализации атрибутов объявлений.
/// 
/// Задача: привести атрибуты из разных источников к единому ключу.
/// Например:
///   makler.md: "Numărul de camere" → "rooms_count"
///   999.md:    feature_id=106      → "rooms_count"  (уже работает через CategoryFilters)
/// 
/// Алгоритм для нового атрибута:
///   1. Ищем в SourceAttributeMappings (кеш предыдущих решений)
///   2. Если не найден — ищем в словаре синонимов (быстро, без БД)
///   3. Если не найден — ищем похожий Key в CategoryFilters по Levenshtein
///   4. Сохраняем результат в SourceAttributeMappings
/// </summary>
public class AttributeNormalizationService
{
    private readonly ILogger<AttributeNormalizationService> _logger;

    // Порог схожести для автоматического маппинга (0.0 - 1.0)
    private const float AutoMapThreshold = 0.75f;

    // Словарь синонимов: сырой ключ (lowercase) → normalized key
    // Покрывает наиболее частые атрибуты недвижимости и транспорта с makler.md
    // Ключи в нижнем регистре без диакритики для нечёткого сравнения
    private static readonly Dictionary<string, string> SynonymMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Недвижимость — румынский
            ["numărul de camere"]        = "rooms_count",
            ["numarul de camere"]        = "rooms_count",
            ["număr de camere"]          = "rooms_count",
            ["numar de camere"]          = "rooms_count",
            ["suprafaţa totală"]         = "total_area",
            ["suprafata totala"]         = "total_area",
            ["suprafaţa totală, m²"]     = "total_area",
            ["suprafata totala, m2"]     = "total_area",
            ["suprafaţa bucătăriei"]     = "kitchen_area",
            ["suprafata bucatariei"]     = "kitchen_area",
            ["etaj"]                     = "floor",
            ["etaje"]                    = "floors_total",
            ["număr de etaje"]           = "floors_total",
            ["tipul clădirii"]           = "building_type",
            ["tipul cladirii"]           = "building_type",
            ["starea apartamentului"]    = "condition",
            ["încălzire"]                = "heating",
            ["incalzire"]                = "heating",
            ["balcon/lojă"]              = "balcony",
            ["balcon/loja"]              = "balcony",
            ["parcare"]                  = "parking",
            ["grup sanitar"]             = "bathroom",
            ["tipul de camere"]          = "room_type",
            ["locul de amplasare"]       = "location_in_building",
            ["sectorul"]                 = "district",

            // Транспорт — румынский
            ["marca"]                    = "brand",
            ["marcă"]                    = "brand",
            ["model"]                    = "model",
            ["anul fabricației"]         = "year",
            ["anul fabricatiei"]         = "year",
            ["an fabricație"]            = "year",
            ["an fabricatie"]            = "year",
            ["rulaj"]                    = "mileage",
            ["capacitatea motorului"]    = "engine_volume",
            ["tip combustibil"]          = "fuel_type",
            ["combustibil"]              = "fuel_type",
            ["cutia de viteze"]          = "transmission",
            ["transmisie"]               = "transmission",
            ["culoare"]                  = "color",
            ["culoarea"]                 = "color",
            ["caroserie"]                = "body_type",
            ["tip caroserie"]            = "body_type",
            ["tracțiune"]                = "drive_type",
            ["tractiune"]                = "drive_type",
            ["puterea motorului"]        = "engine_power",
            ["numărul de locuri"]        = "seats_count",
            ["numar de locuri"]          = "seats_count",
            ["starea tehnica"]           = "technical_condition",
            ["starea tehnică"]           = "technical_condition",

            // Общие
            ["tip anunț"]                = "offer_type",
            ["tip anunt"]                = "offer_type",
            ["tip"]                      = "type",
            ["preţ"]                     = "price",
            ["pret"]                     = "price",
            ["suprafaţa"]                = "area",
            ["suprafata"]                = "area",
        };

    public AttributeNormalizationService(ILogger<AttributeNormalizationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Нормализует атрибуты объявления и сохраняет маппинги в БД.
    /// Возвращает словарь { normalizedKey → value } готовый для ListingAttributes.
    /// </summary>
    public async Task<Dictionary<string, string>> NormalizeAndSaveAsync(
        AppDbContext db,
        int sourceId,
        int? categoryId,
        Dictionary<string, string> rawAttributes,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Загружаем существующие маппинги для этого источника одним запросом
        var existingMappings = await db.SourceAttributeMappings
            .Where(m => m.SourceId == sourceId)
            .ToListAsync(ct);

        // Загружаем CategoryFilters для поиска похожих ключей
        // Берём фильтры текущей категории + глобальные (без привязки к категории)
        var categoryFilters = await db.CategoryFilters
            .Where(f => categoryId == null || f.CategoryId == categoryId)
            .Select(f => f.Key)
            .Distinct()
            .ToListAsync(ct);

        foreach (var (rawKey, rawValue) in rawAttributes)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(rawValue))
                continue;

            var normalizedKey = await ResolveNormalizedKeyAsync(
                db, sourceId, categoryId, rawKey, existingMappings, categoryFilters, ct);

            if (normalizedKey == null)
            {
                // Атрибут помечен как "не нужен" (NormalizedKey = null в БД)
                // или не удалось смаплить — пропускаем
                continue;
            }

            // Нормализуем значение — убираем лишние пробелы
            var normalizedValue = rawValue.Trim();

            result[normalizedKey] = normalizedValue;
        }

        return result;
    }

    /// <summary>
    /// Для одного rawKey находит или создаёт NormalizedKey.
    /// </summary>
    private async Task<string?> ResolveNormalizedKeyAsync(
        AppDbContext db,
        int sourceId,
        int? categoryId,
        string rawKey,
        List<SourceAttributeMapping> existingMappings,
        List<string> categoryFilters,
        CancellationToken ct)
    {
        // 1. Ищем в уже загруженных маппингах
        //    Приоритет: специфичный для категории > глобальный
        var mapping =
            existingMappings.FirstOrDefault(m =>
                m.RawKey.Equals(rawKey, StringComparison.OrdinalIgnoreCase) &&
                m.CategoryId == categoryId)
            ?? existingMappings.FirstOrDefault(m =>
                m.RawKey.Equals(rawKey, StringComparison.OrdinalIgnoreCase) &&
                m.CategoryId == null);

        if (mapping != null)
        {
            // Запись есть — возвращаем NormalizedKey (может быть null если "не нужен")
            return mapping.NormalizedKey;
        }

        // 2. Новый атрибут — пытаемся смаплить автоматически
        var (normalizedKey, confidence) = FindNormalizedKey(rawKey, categoryFilters);

        _logger.LogInformation(
            "AttributeNormalization: новый атрибут [{RawKey}] → [{NormalizedKey}] (confidence={Confidence:F2})",
            rawKey, normalizedKey ?? "null", confidence ?? 0f);

        // 3. Сохраняем маппинг в БД
        var newMapping = new SourceAttributeMapping
        {
            SourceId = sourceId,
            RawKey = rawKey,
            NormalizedKey = normalizedKey,
            CategoryId = null, // глобальный маппинг для источника
            Confidence = confidence,
            IsManual = false
        };

        db.SourceAttributeMappings.Add(newMapping);

        try
        {
            await db.SaveChangesAsync(ct);
            // Добавляем в локальный кеш чтобы не создавать дубли в рамках текущего батча
            existingMappings.Add(newMapping);
        }
        catch (Exception ex)
        {
            // Гонка — другой поток уже создал этот маппинг, игнорируем
            _logger.LogWarning(ex, "AttributeNormalization: дубль маппинга для [{RawKey}], пропускаем", rawKey);
            db.Entry(newMapping).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }

        return normalizedKey;
    }

    /// <summary>
    /// Ищет NormalizedKey через словарь синонимов, затем через Levenshtein по CategoryFilters.
    /// Возвращает (normalizedKey, confidence). normalizedKey может быть null.
    /// </summary>
    private (string? Key, float? Confidence) FindNormalizedKey(string rawKey, List<string> categoryFilters)
    {
        // 1. Точное совпадение в словаре синонимов
        if (SynonymMap.TryGetValue(rawKey, out var exact))
            return (exact, 1.0f);

        // 2. Совпадение в словаре после нормализации (убираем диакритику, lowercase)
        var normalizedRaw = NormalizeString(rawKey);
        var synonymMatch = SynonymMap
            .FirstOrDefault(kv => NormalizeString(kv.Key) == normalizedRaw);
        if (synonymMatch.Value != null)
            return (synonymMatch.Value, 0.95f);

        // 3. Поиск по CategoryFilters через Levenshtein
        if (categoryFilters.Count == 0)
            return (null, 0f);

        var bestKey = "";
        var bestScore = 0f;

        foreach (var filterKey in categoryFilters)
        {
            // Сравниваем нормализованный rawKey с filterKey
            var score = Similarity(normalizedRaw, NormalizeString(filterKey));
            if (score > bestScore)
            {
                bestScore = score;
                bestKey = filterKey;
            }
        }

        if (bestScore >= AutoMapThreshold)
            return (bestKey, bestScore);

        // 4. Не удалось смаплить — сохраняем как null
        return (null, bestScore > 0 ? bestScore : null);
    }

    /// <summary>
    /// Нормализует строку для нечёткого сравнения:
    /// lowercase, убираем диакритику, лишние пробелы.
    /// </summary>
    private static string NormalizeString(string input)
    {
        var result = input.ToLowerInvariant().Trim();

        // Румынские/молдавские символы
        result = result
            .Replace("ă", "a").Replace("â", "a").Replace("î", "i")
            .Replace("ș", "s").Replace("ț", "t")
            .Replace("ş", "s").Replace("ţ", "t")
            .Replace("ê", "e").Replace("é", "e").Replace("è", "e")
            .Replace("ô", "o").Replace("ö", "o")
            .Replace("ü", "u").Replace("ú", "u")
            .Replace(",", "").Replace(".", "").Replace("²", "2");

        // Убираем двойные пробелы
        while (result.Contains("  "))
            result = result.Replace("  ", " ");

        return result;
    }

    /// <summary>
    /// Вычисляет схожесть двух строк от 0.0 до 1.0 через расстояние Левенштейна.
    /// </summary>
    private static float Similarity(string a, string b)
    {
        if (a == b) return 1.0f;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1f - (float)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        for (var j = 1; j <= m; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }

        return d[n, m];
    }
}