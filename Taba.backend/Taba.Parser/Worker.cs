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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.BaseUrl == "https://999.md", stoppingToken);
        
        if (source == null)
        {
            source = new Source { Name = "999.md", BaseUrl = "https://999.md" };
            db.Sources.Add(source);
            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Создан источник 999.md с Id={Id}", source.Id);
        }

        // Получаем дерево категорий
        var categoryTree = await _parser.FetchCategoryTreeAsync();
        if (categoryTree == null)
        {
            _logger.LogError("Не удалось получить дерево категорий");
            return;
        }
        _logger.LogInformation("Root категория: Id={Id}, дочерних={Count}", 
            categoryTree.Id, categoryTree.Categories.Count);

        var leafCategories = _parser.GetLeafCategories(categoryTree)
            //.Where(c => c.Type == "CATEGORY") // только реальные категории
            .Take(20) // для теста берём 20
            .ToList();
        _logger.LogInformation("Найдено {Count} конечных категорий", leafCategories.Count);

        foreach (var category in leafCategories)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation("Парсим категорию {Id} — {Name}",
                category.Id, category.Title?.Translated);
            

            // Находим внутренний ID категории через маппинг
            var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                m => m.SourceId == source.Id && 
                     m.ExternalCategoryName == category.Id.ToString(),
                stoppingToken);
            _logger.LogInformation("Маппинг для категории {ExternalId}: {Result}", 
                category.Id, mapping?.CategoryId.ToString() ?? "НЕ НАЙДЕН");
            
            if (mapping != null)
            {
                await SyncCategoryFiltersAsync(db, mapping.CategoryId, category.Id, stoppingToken);
                await ParseCategoryAsync(db, source, category.Id, mapping.CategoryId, stoppingToken);
            }
            else
            {
                await ParseCategoryAsync(db, source, category.Id, 0, stoppingToken);
            }
        }
    }

    private async Task ParseCategoryAsync(
        AppDbContext db,
        Source source,
        int subCategoryId,
        int internalCategoryId,
        CancellationToken stoppingToken)
    {
        // Строим маппинг featureId → key один раз для всей категории
        var featureKeyMap = await BuildFeatureKeyMapAsync(db, internalCategoryId, stoppingToken);
        _logger.LogInformation("Категория {Id}: загружено {Count} фильтров", 
            subCategoryId, featureKeyMap.Count);

        int skip = 0;
        const int limit = 78;
        const int maxPages = 3;
        int pageCount = 0;
        int totalParsed = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (pageCount >= maxPages) break;

            var ads = await _parser.FetchAdsAsync(subCategoryId, limit, skip);
            if (ads.Count == 0) break;

            foreach (var ad in ads)
                await ProcessAdAsync(db, source, ad, featureKeyMap, stoppingToken);
            
            var pendingAttrs = db.ChangeTracker.Entries<ListingAttribute>()
                .Where(e => e.State == EntityState.Added)
                .Count();
            _logger.LogInformation("Pending ListingAttributes перед сохранением: {Count}", pendingAttrs);

            await db.SaveChangesAsync(stoppingToken);
            totalParsed += ads.Count;
            pageCount++;

            _logger.LogInformation("Категория {SubCategoryId}: страница {Page}, обработано {Count}",
                subCategoryId, pageCount, totalParsed);
            _logger.LogInformation("featureKeyMap для категории {Id}: {Count} ключей: {Keys}", 
                internalCategoryId, featureKeyMap.Count, string.Join(", ", featureKeyMap.Values));

            if (ads.Count < limit) break;
            skip += limit;

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }

        private async Task ProcessAdAsync(
        AppDbContext db,
        Source source,
        Ad ad,
        Dictionary<int, string> featureKeyMap,
        CancellationToken stoppingToken)
    {
        var sellerId = await ResolveSellerAsync(db, source, ad.Owner, stoppingToken);
        var existing = await db.Listings.FirstOrDefaultAsync(
            l => l.SourceId == source.Id && l.ExternalId == ad.Id,
            stoppingToken);

        var (price, currency) = _parser.ExtractPrice(ad);
        var imageUrls = _parser.BuildImageUrls(ad);
        var url = _parser.BuildAdUrl(ad.Id);
        var categoryId = await ResolveListingCategoryAsync(db, source, ad.SubCategory, stoppingToken);

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
                SourceId = source.Id,
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
            db.Listings.Add(listing);
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
        Source source,
        Taba.Parser.Models.NineNineNine.AdCategory? adCategory,
        CancellationToken stoppingToken)
    {
        if (adCategory == null) return null;

        // Проверяем есть ли уже маппинг для этой внешней категории
        var externalCategoryId = adCategory.Id.ToString();

        var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
            m => m.SourceId == source.Id && m.ExternalCategoryName == externalCategoryId,
            stoppingToken);

        if (mapping != null)
            return mapping.CategoryId;

        // Маппинга нет — создаём Category и маппинг

        // Сначала находим или создаём родительские категории
        int? parentId = null;
        if (adCategory.Parent != null)
            parentId = await ResolveListingCategoryAsync(db, source, adCategory.Parent, stoppingToken);

        // Ищем категорию по имени и parentId
        var categoryName = adCategory.Title?.Translated ?? externalCategoryId;

        var category = await db.Categories.FirstOrDefaultAsync(
            c => c.Name == categoryName && c.ParentId == parentId,
            stoppingToken);

        if (category == null)
        {
            category = new Category
            {
                Name = categoryName,
                ParentId = parentId
            };
            db.Categories.Add(category);
            await db.SaveChangesAsync(stoppingToken);
        }

        // Создаём маппинг
        mapping = new SourceCategoryMapping
        {
            SourceId = source.Id,
            ExternalCategoryName = externalCategoryId,
            CategoryId = category.Id
        };
        db.SourceCategoryMappings.Add(mapping);
        await db.SaveChangesAsync(stoppingToken);

        return category.Id;
    }
    private async Task<int?> ResolveSellerAsync(
        AppDbContext db,
        Source source,
        AdOwner? owner,
        CancellationToken stoppingToken)
    {
        if (owner == null) return null;

        var seller = await db.Sellers.FirstOrDefaultAsync(
            s => s.SourceId == source.Id && s.ExternalSellerId == owner.Id,
            stoppingToken);

        if (seller != null)
            return seller.Id;

        seller = new Seller
        {
            SourceId = source.Id,
            ExternalSellerId = owner.Id,
            Name = owner.Login ?? "Неизвестно"
        };

        db.Sellers.Add(seller);
        await db.SaveChangesAsync(stoppingToken);

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
                    var bodyVal = feature.GetProperty("value");
                    if (bodyVal.TryGetProperty("ru", out var ru))
                        listing.Description = ru.GetString();
                    else if (bodyVal.TryGetProperty("translated", out var tr))
                        listing.Description = tr.GetString();
                }
                catch { }
                continue;
            }

            if (featureId == 7) continue;

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
        await db.SaveChangesAsync();
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