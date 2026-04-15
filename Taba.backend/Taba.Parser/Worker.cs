using Microsoft.EntityFrameworkCore;
using Taba.Domain.Entities;
using Taba.Domain.Enums;
using Taba.Infrastucture.Persistence;
using Taba.Parser.Models.NineNineNine;
using Taba.Parser.Parsers;

namespace Taba.Parser;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NineNineNineParser _parser;

    // Интервал между запусками парсера — 1 час
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    // ID категорий 999.md которые хотим парсить
    // Это subCategoryId из запроса — пока берём мониторы для теста
    private readonly int[] _subCategoryIds = { 10 };

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        NineNineNineParser parser)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _parser = parser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Парсер запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunParsingCycleAsync(stoppingToken);
            _logger.LogInformation("Следующий запуск через {Interval}", _interval);
            await Task.Delay(_interval, stoppingToken);
        }
    }

   private async Task RunParsingCycleAsync(CancellationToken stoppingToken)
{
    // Получаем source.Id один раз
    int sourceId;
    using (var initScope = _scopeFactory.CreateScope())
    {
        var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.BaseUrl == "https://999.md", stoppingToken);
        
        if (source == null)
        {
            source = new Source { Name = "999.md", BaseUrl = "https://999.md" };
            db.Sources.Add(source);
            await db.SaveChangesAsync(stoppingToken);
        }
        sourceId = source.Id;
    }

    // Получаем дерево категорий
    var categoryTree = await _parser.FetchCategoryTreeAsync();
    if (categoryTree == null) return;

    var leafCategories = _parser.GetLeafCategories(categoryTree).ToList();

    foreach (var category in leafCategories)
    {
        if (stoppingToken.IsCancellationRequested) break;

        // ✅ Новый scope на каждую категорию — ChangeTracker очищается
        using var categoryScope = _scopeFactory.CreateScope();
        var db = categoryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.AutoDetectChangesEnabled = false; // ✅

        _logger.LogInformation("Парсим категорию {Id} — {Name}",
            category.Id, category.Title?.Translated);

        var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
            m => m.SourceId == sourceId &&
                 m.ExternalCategoryName == category.Id.ToString(),
            stoppingToken);

        if (mapping != null)
            await SyncCategoryFiltersAsync(db, mapping.CategoryId, category.Id, stoppingToken);

        await ParseCategoryAsync(db, sourceId, category.Id, mapping?.CategoryId ?? 0, stoppingToken);

        if (mapping == null)
        {
            mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                m => m.SourceId == sourceId && 
                     m.ExternalCategoryName == category.Id.ToString(), stoppingToken);
            if (mapping != null)
                await SyncCategoryFiltersAsync(db, mapping.CategoryId, category.Id, stoppingToken);
        }
        
        // ✅ После выхода из using — DbContext и все отслеживаемые объекты освобождаются
    }
}

private async Task ParseCategoryAsync(
    AppDbContext db,
    int sourceId,       // ✅ передаём ID, а не объект
    int subCategoryId,
    int internalCategoryId,
    CancellationToken stoppingToken)
{
    var featureKeyMap = await BuildFeatureKeyMapAsync(db, internalCategoryId, stoppingToken);
    var sellerCache = new Dictionary<string, int>(); 
    int skip = 0;
    const int limit = 78;
    const int maxPages = 3;
    int pageCount = 0;
    bool featureMapBuilt = internalCategoryId != 0;

    while (!stoppingToken.IsCancellationRequested && pageCount < maxPages)
    {
        var ads = await _parser.FetchAdsAsync(subCategoryId, limit, skip);
        if (ads.Count == 0) break;

        foreach (var ad in ads)
        {
            await ProcessAdAsync(db, sourceId, ad, featureKeyMap, sellerCache, stoppingToken);

            if (!featureMapBuilt)
            {
                var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                    m => m.SourceId == sourceId &&
                         m.ExternalCategoryName == subCategoryId.ToString(), stoppingToken);
                if (mapping != null)
                {
                    await SyncCategoryFiltersAsync(db, mapping.CategoryId, subCategoryId, stoppingToken);
                    featureKeyMap = await BuildFeatureKeyMapAsync(db, mapping.CategoryId, stoppingToken);
                    featureMapBuilt = true;
                }
            }
        }

        // ✅ Сохраняем один раз за страницу, а не на каждое объявление
        await db.SaveChangesAsync(stoppingToken);
        
        // ✅ Очищаем ChangeTracker после сохранения батча
        db.ChangeTracker.Clear();

        _logger.LogInformation("Категория {SubCategoryId}: страница {Page}, skip={Skip}",
            subCategoryId, pageCount + 1, skip);

        if (ads.Count < limit) break;
        skip += limit;
        pageCount++;

        await Task.Delay(500, stoppingToken);
    }
}

        private async Task ProcessAdAsync(
        AppDbContext db,
        int sourceId, 
        Ad ad,
        Dictionary<int, string> featureKeyMap,
        Dictionary<string, int> sellerCache, 
        CancellationToken stoppingToken)
    {
        var sellerId = await ResolveSellerAsync(db, sourceId, ad.Owner,sellerCache, stoppingToken);
        var existing = await db.Listings.FirstOrDefaultAsync(
            l => l.SourceId == sourceId && l.ExternalId == ad.Id,
            stoppingToken);

        var (price, currency) = _parser.ExtractPrice(ad);
        var imageUrls = _parser.BuildImageUrls(ad);
        var url = _parser.BuildAdUrl(ad.Id);
        var categoryId = await ResolveListingCategoryAsync(db, sourceId, ad.SubCategory, stoppingToken);

        if (existing != null)
        {
            if (existing.Price != price)
            {
                db.ListingPriceHistories.Add(new ListingPriceHistory
                {
                    ListingId = existing.Id,
                    Price = price,
                    Currency = currency,
                    RecordedAt = DateTime.UtcNow
                });
            }

            if (categoryId.HasValue)
            {
                var existingCategory = await db.ListingCategories.AnyAsync(
                    lc => lc.ListingId == existing.Id, stoppingToken);
                if (!existingCategory)
                {
                    db.ListingCategories.Add(new ListingCategory
                    {
                        ListingId = existing.Id,
                        CategoryId = categoryId.Value,
                        Source = ListingCategorySource.Mapping,
                        Confidence = 1.0f
                    });
                }
            }

            if (sellerId.HasValue)
                existing.SellerId = sellerId;

            var hasImages = await db.ListingImages.AnyAsync(
                i => i.ListingId == existing.Id, stoppingToken);
            if (!hasImages)
            {
                for (int i = 0; i < imageUrls.Count; i++)
                {
                    db.ListingImages.Add(new ListingImage
                    {
                        ListingId = existing.Id,
                        Url = imageUrls[i],
                        OrderIndex = i
                    });
                }
            }
            if (string.IsNullOrEmpty(existing.Description) || existing.RegionId == null)
            {
                var details = await _parser.FetchAdDetailsAsync(ad.Id);
                await SaveListingAttributesAsync(db, existing.Id, existing, details, featureKeyMap);
                await Task.Delay(200, stoppingToken);
            }

            existing.Price = price;
            existing.Currency = currency;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ParsedAt = DateTime.UtcNow;
            existing.Status = ListingStatus.Active;
        }
        else
        {
            var listing = new Listing
            {
                SourceId = sourceId,
                ExternalId = ad.Id,
                Title = ad.Title ?? string.Empty,
                Price = price,
                Currency = currency,
                Url = url,
                SellerId = sellerId,
                Status = ListingStatus.Active,
                CountryId = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ParsedAt = DateTime.UtcNow
            };
            _logger.LogInformation("Создаём listing: ExternalId={Id}, Currency={Currency}, RawRegionName={Region}",
                ad.Id, currency, "будет из деталей");
            db.Listings.Add(listing);
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(stoppingToken);

            var details = await _parser.FetchAdDetailsAsync(ad.Id);
            _logger.LogInformation("Детали объявления {Id}: получено {Count} features: {Keys}", 
                ad.Id, details.Count, string.Join(", ", details.Keys));
            await SaveListingAttributesAsync(db, listing.Id, listing, details, featureKeyMap);
            await Task.Delay(200, stoppingToken);

            if (categoryId.HasValue)
            {
                db.ListingCategories.Add(new ListingCategory
                {
                    ListingId = listing.Id,
                    CategoryId = categoryId.Value,
                    Source = ListingCategorySource.Mapping,
                    Confidence = 1.0f
                });
            }

            for (int i = 0; i < imageUrls.Count; i++)
            {
                db.ListingImages.Add(new ListingImage
                {
                    ListingId = listing.Id,
                    Url = imageUrls[i],
                    OrderIndex = i
                });
            }

            db.ListingPriceHistories.Add(new ListingPriceHistory
            {
                ListingId = listing.Id,
                Price = price,
                Currency = currency,
                RecordedAt = DateTime.UtcNow
            });
        }
    }
    private async Task<int?> ResolveListingCategoryAsync(
        AppDbContext db,
        int sourceId,
        Taba.Parser.Models.NineNineNine.AdCategory? adCategory,
        CancellationToken stoppingToken)
    {
        if (adCategory == null) return null;

        var externalCategoryId = adCategory.Id.ToString();

        var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
            m => m.SourceId == sourceId && m.ExternalCategoryName == externalCategoryId,
            stoppingToken);

        if (mapping != null)
            return mapping.CategoryId;

        // Сначала рекурсивно создаём родителя
        int? parentId = null;
        if (adCategory.Parent != null)
            parentId = await ResolveListingCategoryAsync(db, sourceId, adCategory.Parent, stoppingToken);

        var categoryName = adCategory.Title?.Translated ?? externalCategoryId;

        var category = await db.Categories.FirstOrDefaultAsync(
            c => c.Name == categoryName && c.ParentId == parentId,
            stoppingToken);

        if (category == null)
        {
            category = new Category { Name = categoryName, ParentId = parentId };
            db.Categories.Add(category);
            // ✅ Сохраняем сразу — нам нужен реальный Id для FK
            await db.SaveChangesAsync(stoppingToken);
        }

        mapping = new SourceCategoryMapping
        {
            SourceId = sourceId,
            ExternalCategoryName = externalCategoryId,
            CategoryId = category.Id
        };
        db.SourceCategoryMappings.Add(mapping);
        // ✅ Сохраняем сразу — маппинг тоже нужен немедленно
        await db.SaveChangesAsync(stoppingToken);

        return category.Id;
    }
    private async Task<int?> ResolveSellerAsync(
        AppDbContext db,
        int sourceId,
        AdOwner? owner,
        Dictionary<string, int> sellerCache,   // ✅ кеш
        CancellationToken stoppingToken)
    {
        if (owner == null) return null;
    
        if (sellerCache.TryGetValue(owner.Id, out var cachedId))
            return cachedId;

        var seller = await db.Sellers.FirstOrDefaultAsync(
            s => s.SourceId == sourceId && s.ExternalSellerId == owner.Id,
            stoppingToken);

        if (seller != null)
        {
            sellerCache[owner.Id] = seller.Id;
            return seller.Id;
        }

        seller = new Seller { SourceId = sourceId, ExternalSellerId = owner.Id, Name = owner.Login ?? "Неизвестно" };
        db.Sellers.Add(seller);
        await db.SaveChangesAsync(stoppingToken);
        sellerCache[owner.Id] = seller.Id;
        return seller.Id;
    }
    private async Task<Dictionary<int, string>> BuildFeatureKeyMapAsync(
        AppDbContext db,
        int categoryId,
        CancellationToken stoppingToken)
    {
        return await db.CategoryFilters
            .Where(f => f.CategoryId == categoryId && f.SourceFeatureId != null)
            .ToDictionaryAsync(
                f => f.SourceFeatureId!.Value,
                f => f.Key,
                stoppingToken);
    }

    private async Task SaveListingAttributesAsync(
        AppDbContext db,
        int listingId,
        Listing listing, // передаём уже отслеживаемый объект
        Dictionary<int, System.Text.Json.JsonElement> details,
        Dictionary<int, string> featureKeyMap)
    {
        _logger.LogInformation("=== SaveListingAttributes ВЫЗВАН: listingId={Id}, featureKeyMap.Count={Count}", 
            listingId, featureKeyMap.Count);
        foreach (var (featureId, feature) in details)
        {
            if (featureId == 13)
            {
                try
                {
                    _logger.LogInformation("Feature 13 raw value: {Json}", feature.ToString());
                    var bodyVal = feature.GetProperty("value");
                    if (bodyVal.TryGetProperty("ru", out var ru))
                        listing.Description = ru.GetString();
                    else if (bodyVal.TryGetProperty("translated", out var tr))
                        listing.Description = tr.GetString();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка парсинга Description для listing {Id}", listingId);
                }
                continue;
            }

            if (featureId == 7)
            {
                try
                {
                    var bodyVal = feature.GetProperty("value");
                    if (bodyVal.TryGetProperty("translated", out var regionName))
                    {
                        var name = regionName.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            // Ищем или создаём регион
                            var region = await db.Regions.FirstOrDefaultAsync(r => r.Name == name);
                            if (region == null)
                            {
                                region = new Region { Name = name, CountryId = listing.CountryId };
                                db.Regions.Add(region);
                                await db.SaveChangesAsync();
                            }
                            listing.RegionId = region.Id;
                            _logger.LogInformation("RegionName длина={Len}, значение={Name}", name?.Length, name);
                            listing.RawRegionName = name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка парсинга региона для listing {Id}", listingId);
                }
                continue;
            }

            if (!featureKeyMap.TryGetValue(featureId, out var key))
            {
                _logger.LogWarning("featureId {FId} не найден. Доступные ключи: {Keys}", 
                    featureId, string.Join(",", featureKeyMap.Keys));
                continue;
            }

            var value = _parser.ExtractFeatureValue(feature);
            if (string.IsNullOrWhiteSpace(value)) continue;

            db.ListingAttributes.Add(new ListingAttribute
            {
                ListingId = listingId,
                Key = key,
                Value = value
            });
            _logger.LogInformation("=== Добавлен атрибут {Key}={Value} для listing {Id}", key, value, listingId);
           
        }
        //await db.SaveChangesAsync();
        db.Entry(listing).State = EntityState.Modified;
        _logger.LogInformation("Атрибуты сохранены для listing {Id}", listingId);
    }
    // Типы фильтров которые пропускаем — системные, не нужны пользователю
    private static readonly HashSet<string> SkipFilterTypes = new()
    {
        "FILTER_TYPE_EXISTS",  // "Объявления с видео"
    };

    // Feature типы которые пропускаем
    private static readonly HashSet<string> SkipFeatureTypes = new()
    {
        "FEATURE_OFFER_TYPE",  // Тип предложения (Продаю/Куплю) — не атрибут товара
        "FEATURE_PRICE",       // Цена — уже есть отдельно
        "FEATURE_IMAGES",
        "FEATURE_VIDEOS",
        "FEATURE_BODY",        // Описание
    };

    private async Task SyncCategoryFiltersAsync(
        AppDbContext db,
        int categoryId,
        int sourceExternalCategoryId,
        CancellationToken stoppingToken)
    {
        var nineFilters = await _parser.FetchCategoryFiltersAsync(sourceExternalCategoryId);
        if (nineFilters.Count == 0) return;

        int sortOrder = 0;
        foreach (var filter in nineFilters)
        {
            if (SkipFilterTypes.Contains(filter.Type)) continue;

            // Берём первый feature у фильтра — обычно он один
            var feature = filter.Features.FirstOrDefault();
            if (feature == null) continue;
            if (SkipFeatureTypes.Contains(feature.Type)) continue;

            // Проверяем не существует ли уже такой фильтр
            var exists = await db.CategoryFilters.AnyAsync(
                f => f.CategoryId == categoryId && f.SourceFeatureId == feature.Id,
                stoppingToken);
            if (exists) continue;

            // Определяем тип фильтра
            var filterType = filter.Type switch
            {
                "FILTER_TYPE_RANGE" => "range",
                "FILTER_TYPE_OPTIONS" => "select",
                "FILTER_TYPE_FEATURES_AND" => "checkbox",
                _ => "select"
            };

            // Генерируем ключ из названия фильтра
            var key = filter.Title?.Translated?
                .ToLower()
                .Replace(" ", "_")
                .Replace(".", "")
                ?? $"feature_{feature.Id}";

            // Собираем опции если есть
            string? options = null;
            if (feature.Options.Count > 0)
            {
                var optionValues = feature.Options
                    .Where(o => o.Title?.Translated != null)
                    .Select(o => o.Title!.Translated!)
                    .ToList();
                var jsonOptions = new System.Text.Json.JsonSerializerOptions 
                { 
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };
                if (optionValues.Count > 0)
                    options = System.Text.Json.JsonSerializer.Serialize(optionValues, jsonOptions);
            }

            db.CategoryFilters.Add(new Taba.Domain.Entities.CategoryFilter
            {
                CategoryId = categoryId,
                Key = key,
                Label = filter.Title?.Translated ?? key,
                FilterType = filterType,
                Options = options,
                SourceFeatureId = feature.Id,
                IsInherited = false,
                SortOrder = sortOrder++
            });
        }

        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Синхронизированы фильтры для категории {CategoryId}", categoryId);
    }
}