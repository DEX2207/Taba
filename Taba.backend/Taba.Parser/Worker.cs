using Microsoft.EntityFrameworkCore;
using Taba.Domain.Entities;
using Taba.Domain.Enums;
using Taba.Infrastucture.Persistence;
using Taba.Parser.Models.NineNineNine;
using Taba.Parser.Parsers;
using Taba.Parser.Services;

namespace Taba.Parser;

public class Worker : BackgroundService, IAsyncDisposable
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NineNineNineParser _nineParser;
    private readonly MaklerParser _maklerParser;

    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        NineNineNineParser nineParser,
        MaklerParser maklerParser)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _nineParser = nineParser;
        _maklerParser = maklerParser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Парсер запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            //await RunNineParsingCycleAsync(stoppingToken);
            await RunMaklerParsingCycleAsync(stoppingToken);

            _logger.LogInformation("Следующий запуск через {Interval}", _interval);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MAKLER.MD
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunMaklerParsingCycleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Запуск цикла makler.md ===");
 
        await _maklerParser.InitializeAsync();
 
        int sourceId;
        using (var initScope = _scopeFactory.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.Sources.FirstOrDefaultAsync(
                s => s.BaseUrl == "https://makler.md", stoppingToken);
 
            if (source == null)
            {
                source = new Source { Name = "makler.md", BaseUrl = "https://makler.md" };
                db.Sources.Add(source);
                await db.SaveChangesAsync(stoppingToken);
            }
            sourceId = source.Id;
        }
 
        // ✅ Динамически получаем категории с сайта вместо хардкода
        var categories = await _maklerParser.FetchCategoryTreeAsync();
        if (categories.Count == 0)
        {
            _logger.LogError("Makler: не удалось получить категории, пропускаем цикл");
            return;
        }
 
        _logger.LogInformation("Makler: получено {Count} категорий для парсинга", categories.Count);
 
        foreach (var category in categories)
        {
            if (stoppingToken.IsCancellationRequested) break;
 
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var normService = scope.ServiceProvider.GetRequiredService<AttributeNormalizationService>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
 
            _logger.LogInformation("Makler: парсим категорию «{Name}» ({Slug})",
                category.Name, category.Slug);
 
            await ParseMaklerCategoryAsync(db, normService, sourceId, category, stoppingToken);
        }
    }

    private async Task ParseMaklerCategoryAsync(
        AppDbContext db,
        AttributeNormalizationService normService,
        int sourceId,
        Models.Makler.MaklerCategory category,
        CancellationToken stoppingToken)
    {
        const int maxPages = 5;

        var internalCategoryId = await ResolveMaklerCategoryAsync(db, sourceId, category, stoppingToken);

        for (int page = 0; page < maxPages; page++)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var items = await _maklerParser.FetchListingPageAsync(category.Slug, page);
            if (items.Count == 0)
            {
                _logger.LogInformation("Makler: категория {Slug} стр.{Page} — объявлений нет, стоп",
                    category.Slug, page);
                break;
            }

            var sellerCache = new Dictionary<string, int>();

            foreach (var item in items)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await ProcessMaklerListingAsync(
                    db, normService, sourceId, internalCategoryId, category.Slug, item, sellerCache, stoppingToken);
            }

            await db.SaveChangesAsync(stoppingToken);
            db.ChangeTracker.Clear();

            _logger.LogInformation("Makler: категория {Slug} стр.{Page} — обработано {Count} объявлений",
                category.Slug, page, items.Count);

            if (items.Count < 20) break;

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessMaklerListingAsync(
        AppDbContext db,
        AttributeNormalizationService normService,
        int sourceId,
        int? internalCategoryId,
        string categorySlug,
        Models.Makler.MaklerListingItem item,
        Dictionary<string, int> sellerCache,
        CancellationToken stoppingToken)
    {
        var existing = await db.Listings.FirstOrDefaultAsync(
            l => l.SourceId == sourceId && l.ExternalId == item.Id,
            stoppingToken);

        var price = item.Price ?? 0m;
        var currency = item.Currency;

        if (existing != null)
        {
            if (existing.Price != price && price > 0)
            {
                db.ListingPriceHistories.Add(new ListingPriceHistory
                {
                    ListingId = existing.Id,
                    Price = price,
                    Currency = currency,
                    RecordedAt = DateTime.UtcNow
                });
            }

            existing.Price = price;
            existing.Currency = currency;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ParsedAt = DateTime.UtcNow;
            existing.Status = ListingStatus.Active;

            if (string.IsNullOrEmpty(existing.Description))
            {
                await EnrichMaklerListingAsync(
                    db, normService, existing, item.Url, sellerCache, sourceId, internalCategoryId, stoppingToken);
            }
        }
        else
        {
            var listing = new Listing
            {
                SourceId = sourceId,
                ExternalId = item.Id,
                Title = item.Title,
                Price = price,
                Currency = currency,
                Url = item.Url,
                Status = ListingStatus.Active,
                CountryId = 1,
                RawRegionName = item.City,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ParsedAt = DateTime.UtcNow
            };

            db.Listings.Add(listing);
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(stoppingToken);

            if (internalCategoryId.HasValue)
            {
                db.ListingCategories.Add(new ListingCategory
                {
                    ListingId = listing.Id,
                    CategoryId = internalCategoryId.Value,
                    Source = ListingCategorySource.Mapping,
                    Confidence = 1.0f
                });
            }

            if (!string.IsNullOrEmpty(item.PreviewImageUrl))
            {
                db.ListingImages.Add(new ListingImage
                {
                    ListingId = listing.Id,
                    Url = item.PreviewImageUrl,
                    OrderIndex = 0
                });
            }

            if (price > 0)
            {
                db.ListingPriceHistories.Add(new ListingPriceHistory
                {
                    ListingId = listing.Id,
                    Price = price,
                    Currency = currency,
                    RecordedAt = DateTime.UtcNow
                });
            }

            await EnrichMaklerListingAsync(
                db, normService, listing, item.Url, sellerCache, sourceId, internalCategoryId, stoppingToken);
        }
    }

    private async Task EnrichMaklerListingAsync(
        AppDbContext db,
        AttributeNormalizationService normService,
        Listing listing,
        string url,
        Dictionary<string, int> sellerCache,
        int sourceId,
        int? categoryId,
        CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800), stoppingToken);

        var detail = await _maklerParser.FetchListingDetailAsync(url);
        if (detail == null) return;

        if (listing.Price == 0 && detail.Price.HasValue && detail.Price.Value > 0)
        {
            listing.Price = detail.Price.Value;
            listing.Currency = detail.Currency;
        }
        // Описание
        if (!string.IsNullOrWhiteSpace(detail.Description))
            listing.Description = detail.Description;

        // Регион
        var regionName = detail.City;
        if (string.IsNullOrEmpty(regionName) && detail.District != null)
            regionName = detail.District;

        if (!string.IsNullOrWhiteSpace(regionName))
        {
            listing.RawRegionName = regionName;
            var region = await db.Regions.FirstOrDefaultAsync(r => r.Name == regionName, stoppingToken);
            if (region == null)
            {
                region = new Region { Name = regionName, CountryId = listing.CountryId };
                db.Regions.Add(region);
                await db.SaveChangesAsync(stoppingToken);
            }
            listing.RegionId = region.Id;
        }

        // Продавец
        if (detail.ExternalSellerId != null)
        {
            var sellerId = await ResolveMaklerSellerAsync(
                db, sourceId, detail.ExternalSellerId, detail.SellerName, detail.Phone, sellerCache, stoppingToken);
            if (sellerId.HasValue)
                listing.SellerId = sellerId;
        }

        // Фото
        var hasImages = await db.ListingImages.AnyAsync(i => i.ListingId == listing.Id, stoppingToken);
        if (!hasImages || detail.ImageUrls.Count > 1)
        {
            var existingUrls = await db.ListingImages
                .Where(i => i.ListingId == listing.Id)
                .Select(i => i.Url)
                .ToListAsync(stoppingToken);

            for (int i = 0; i < detail.ImageUrls.Count; i++)
            {
                var imgUrl = detail.ImageUrls[i];
                if (!existingUrls.Contains(imgUrl))
                {
                    db.ListingImages.Add(new ListingImage
                    {
                        ListingId = listing.Id,
                        Url = imgUrl,
                        OrderIndex = existingUrls.Count + i
                    });
                }
            }
        }

        if (detail.Attributes.Count > 0)
        {
            var normalized = await normService.NormalizeAndSaveAsync(
                db, sourceId, categoryId, detail.Attributes, stoppingToken);

            foreach (var (key, value) in normalized)
            {
                var alreadyExists = await db.ListingAttributes.AnyAsync(
                    a => a.ListingId == listing.Id && a.Key == key, stoppingToken);

                if (!alreadyExists)
                {
                    db.ListingAttributes.Add(new ListingAttribute
                    {
                        ListingId = listing.Id,
                        Key = key,
                        Value = value
                    });
                }
            }
        }

        db.Entry(listing).State = EntityState.Modified;

        _logger.LogInformation(
            "Makler: обогащено объявление {Id} — {AttrCount} атрибутов, {ImgCount} фото",
            listing.ExternalId, detail.Attributes.Count, detail.ImageUrls.Count);
    }

    private async Task<int?> ResolveMaklerSellerAsync(
        AppDbContext db,
        int sourceId,
        string externalSellerId,
        string? sellerName,
        string? phone,
        Dictionary<string, int> cache,
        CancellationToken stoppingToken)
    {
        if (cache.TryGetValue(externalSellerId, out var cached))
            return cached;

        var seller = await db.Sellers.FirstOrDefaultAsync(
            s => s.SourceId == sourceId && s.ExternalSellerId == externalSellerId,
            stoppingToken);

        if (seller == null)
        {
            seller = new Seller
            {
                SourceId = sourceId,
                ExternalSellerId = externalSellerId,
                Name = sellerName ?? "Неизвестно",
                Phone = phone
            };
            db.Sellers.Add(seller);
            await db.SaveChangesAsync(stoppingToken);
        }

        cache[externalSellerId] = seller.Id;
        return seller.Id;
    }

    private async Task<int?> ResolveMaklerCategoryAsync(
    AppDbContext db,
    int sourceId,
    Models.Makler.MaklerCategory category,
    CancellationToken stoppingToken)
    {
        // Ищем уже существующий маппинг
        var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
            m => m.SourceId == sourceId && m.ExternalCategoryName == category.Slug,
            stoppingToken);
     
        if (mapping != null)
            return mapping.CategoryId;
     
        // Резолвим раздел (верхний уровень: Недвижимость, Транспорт...)
        var sectionId = await ResolveOrCreateCategoryAsync(
            db, sourceId, category.SectionSlug, category.SectionName, null, stoppingToken);
     
        // Резолвим родителя если он отличается от раздела
        int? parentId = sectionId;
        if (!string.IsNullOrEmpty(category.ParentSlug) &&
            category.ParentSlug != category.SectionSlug)
        {
            // Ищем маппинг родителя
            var parentMapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                m => m.SourceId == sourceId && m.ExternalCategoryName == category.ParentSlug,
                stoppingToken);
     
            parentId = parentMapping?.CategoryId ?? sectionId;
        }
     
        // Создаём саму категорию
        var catId = await ResolveOrCreateCategoryAsync(
            db, sourceId, category.Slug, category.Name, parentId, stoppingToken);
     
        return catId;
    }
 
/// <summary>
/// Ищет или создаёт Category и SourceCategoryMapping для указанного slug.
/// </summary>
private async Task<int?> ResolveOrCreateCategoryAsync(
    AppDbContext db,
    int sourceId,
    string slug,
    string name,
    int? parentId,
    CancellationToken stoppingToken)
{
    // Уже есть маппинг?
    var existing = await db.SourceCategoryMappings.FirstOrDefaultAsync(
        m => m.SourceId == sourceId && m.ExternalCategoryName == slug,
        stoppingToken);
    if (existing != null) return existing.CategoryId;
 
    // Ищем категорию по имени и родителю
    var cat = await db.Categories.FirstOrDefaultAsync(
        c => c.Name == name && c.ParentId == parentId,
        stoppingToken);
 
    if (cat == null)
    {
        cat = new Category { Name = name, ParentId = parentId };
        db.Categories.Add(cat);
        await db.SaveChangesAsync(stoppingToken);
    }
 
    db.SourceCategoryMappings.Add(new SourceCategoryMapping
    {
        SourceId = sourceId,
        ExternalCategoryName = slug,
        CategoryId = cat.Id
    });
    await db.SaveChangesAsync(stoppingToken);
 
    return cat.Id;
}
    // ═══════════════════════════════════════════════════════════════════════
    // 999.MD
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunNineParsingCycleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Запуск цикла 999.md ===");

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

        var categoryTree = await _nineParser.FetchCategoryTreeAsync();
        if (categoryTree == null) return;

        var leafCategories = _nineParser.GetLeafCategories(categoryTree).ToList();

        foreach (var category in leafCategories)
        {
            if (stoppingToken.IsCancellationRequested) break;

            using var categoryScope = _scopeFactory.CreateScope();
            var db = categoryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            _logger.LogInformation("999.md: парсим категорию {Id} — {Name}",
                category.Id, category.Title?.Translated);

            var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                m => m.SourceId == sourceId && m.ExternalCategoryName == category.Id.ToString(),
                stoppingToken);

            if (mapping != null)
                await SyncCategoryFiltersAsync(db, mapping.CategoryId, category.Id, stoppingToken);

            await ParseNineCategoryAsync(db, sourceId, category.Id, mapping?.CategoryId ?? 0, stoppingToken);

            if (mapping == null)
            {
                mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                    m => m.SourceId == sourceId && m.ExternalCategoryName == category.Id.ToString(), stoppingToken);
                if (mapping != null)
                    await SyncCategoryFiltersAsync(db, mapping.CategoryId, category.Id, stoppingToken);
            }
        }
    }

    private async Task ParseNineCategoryAsync(
        AppDbContext db,
        int sourceId,
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
            var ads = await _nineParser.FetchAdsAsync(subCategoryId, limit, skip);
            if (ads.Count == 0) break;

            foreach (var ad in ads)
            {
                await ProcessNineAdAsync(db, sourceId, ad, featureKeyMap, sellerCache, stoppingToken);

                if (!featureMapBuilt)
                {
                    var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
                        m => m.SourceId == sourceId && m.ExternalCategoryName == subCategoryId.ToString(),
                        stoppingToken);
                    if (mapping != null)
                    {
                        await SyncCategoryFiltersAsync(db, mapping.CategoryId, subCategoryId, stoppingToken);
                        featureKeyMap = await BuildFeatureKeyMapAsync(db, mapping.CategoryId, stoppingToken);
                        featureMapBuilt = true;
                    }
                }
            }

            await db.SaveChangesAsync(stoppingToken);
            db.ChangeTracker.Clear();

            _logger.LogInformation("999.md: категория {SubCategoryId}: страница {Page}, skip={Skip}",
                subCategoryId, pageCount + 1, skip);

            if (ads.Count < limit) break;
            skip += limit;
            pageCount++;

            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task ProcessNineAdAsync(
        AppDbContext db,
        int sourceId,
        Ad ad,
        Dictionary<int, string> featureKeyMap,
        Dictionary<string, int> sellerCache,
        CancellationToken stoppingToken)
    {
        var sellerId = await ResolveNineSellerAsync(db, sourceId, ad.Owner, sellerCache, stoppingToken);
        var existing = await db.Listings.FirstOrDefaultAsync(
            l => l.SourceId == sourceId && l.ExternalId == ad.Id, stoppingToken);

        var (price, currency) = _nineParser.ExtractPrice(ad);
        var imageUrls = _nineParser.BuildImageUrls(ad);
        var url = _nineParser.BuildAdUrl(ad.Id);
        var categoryId = await ResolveNineListingCategoryAsync(db, sourceId, ad.SubCategory, stoppingToken);

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

            if (sellerId.HasValue) existing.SellerId = sellerId;

            var hasImages = await db.ListingImages.AnyAsync(i => i.ListingId == existing.Id, stoppingToken);
            if (!hasImages)
            {
                for (int i = 0; i < imageUrls.Count; i++)
                    db.ListingImages.Add(new ListingImage
                        { ListingId = existing.Id, Url = imageUrls[i], OrderIndex = i });
            }

            if (string.IsNullOrEmpty(existing.Description) || existing.RegionId == null)
            {
                var details = await _nineParser.FetchAdDetailsAsync(ad.Id);
                await SaveNineListingAttributesAsync(db, existing.Id, existing, details, featureKeyMap);
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

            db.Listings.Add(listing);
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(stoppingToken);

            var details = await _nineParser.FetchAdDetailsAsync(ad.Id);
            await SaveNineListingAttributesAsync(db, listing.Id, listing, details, featureKeyMap);
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
                db.ListingImages.Add(new ListingImage
                    { ListingId = listing.Id, Url = imageUrls[i], OrderIndex = i });

            db.ListingPriceHistories.Add(new ListingPriceHistory
            {
                ListingId = listing.Id,
                Price = price,
                Currency = currency,
                RecordedAt = DateTime.UtcNow
            });
        }
    }

    private async Task<int?> ResolveNineListingCategoryAsync(
        AppDbContext db, int sourceId, AdCategory? adCategory, CancellationToken stoppingToken)
    {
        if (adCategory == null) return null;
        var externalCategoryId = adCategory.Id.ToString();
        var mapping = await db.SourceCategoryMappings.FirstOrDefaultAsync(
            m => m.SourceId == sourceId && m.ExternalCategoryName == externalCategoryId, stoppingToken);
        if (mapping != null) return mapping.CategoryId;

        int? parentId = null;
        if (adCategory.Parent != null)
            parentId = await ResolveNineListingCategoryAsync(db, sourceId, adCategory.Parent, stoppingToken);

        var categoryName = adCategory.Title?.Translated ?? externalCategoryId;
        var category = await db.Categories.FirstOrDefaultAsync(
            c => c.Name == categoryName && c.ParentId == parentId, stoppingToken);

        if (category == null)
        {
            category = new Category { Name = categoryName, ParentId = parentId };
            db.Categories.Add(category);
            await db.SaveChangesAsync(stoppingToken);
        }

        mapping = new SourceCategoryMapping
        {
            SourceId = sourceId,
            ExternalCategoryName = externalCategoryId,
            CategoryId = category.Id
        };
        db.SourceCategoryMappings.Add(mapping);
        await db.SaveChangesAsync(stoppingToken);
        return category.Id;
    }

    private async Task<int?> ResolveNineSellerAsync(
        AppDbContext db, int sourceId, AdOwner? owner,
        Dictionary<string, int> sellerCache, CancellationToken stoppingToken)
    {
        if (owner == null) return null;
        if (sellerCache.TryGetValue(owner.Id, out var cachedId)) return cachedId;

        var seller = await db.Sellers.FirstOrDefaultAsync(
            s => s.SourceId == sourceId && s.ExternalSellerId == owner.Id, stoppingToken);

        if (seller == null)
        {
            seller = new Seller
            {
                SourceId = sourceId,
                ExternalSellerId = owner.Id,
                Name = owner.Login ?? "Неизвестно"
            };
            db.Sellers.Add(seller);
            await db.SaveChangesAsync(stoppingToken);
        }

        sellerCache[owner.Id] = seller.Id;
        return seller.Id;
    }

    private async Task<Dictionary<int, string>> BuildFeatureKeyMapAsync(
        AppDbContext db, int categoryId, CancellationToken stoppingToken)
    {
        return await db.CategoryFilters
            .Where(f => f.CategoryId == categoryId && f.SourceFeatureId != null)
            .ToDictionaryAsync(f => f.SourceFeatureId!.Value, f => f.Key, stoppingToken);
    }

    private async Task SaveNineListingAttributesAsync(
        AppDbContext db, int listingId, Listing listing,
        Dictionary<int, System.Text.Json.JsonElement> details,
        Dictionary<int, string> featureKeyMap)
    {
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка Description для listing {Id}", listingId);
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
                            var region = await db.Regions.FirstOrDefaultAsync(r => r.Name == name);
                            if (region == null)
                            {
                                region = new Region { Name = name, CountryId = listing.CountryId };
                                db.Regions.Add(region);
                                await db.SaveChangesAsync();
                            }
                            listing.RegionId = region.Id;
                            listing.RawRegionName = name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка региона для listing {Id}", listingId);
                }
                continue;
            }

            if (!featureKeyMap.TryGetValue(featureId, out var key)) continue;
            var value = _nineParser.ExtractFeatureValue(feature);
            if (string.IsNullOrWhiteSpace(value)) continue;

            db.ListingAttributes.Add(new ListingAttribute { ListingId = listingId, Key = key, Value = value });
        }

        db.Entry(listing).State = EntityState.Modified;
    }

    private static readonly HashSet<string> SkipFilterTypes = new() { "FILTER_TYPE_EXISTS" };
    private static readonly HashSet<string> SkipFeatureTypes = new()
    {
        "FEATURE_OFFER_TYPE", "FEATURE_PRICE", "FEATURE_IMAGES", "FEATURE_VIDEOS", "FEATURE_BODY"
    };

    private async Task SyncCategoryFiltersAsync(
        AppDbContext db, int categoryId, int sourceExternalCategoryId, CancellationToken stoppingToken)
    {
        var nineFilters = await _nineParser.FetchCategoryFiltersAsync(sourceExternalCategoryId);
        if (nineFilters.Count == 0) return;

        int sortOrder = 0;
        foreach (var filter in nineFilters)
        {
            if (SkipFilterTypes.Contains(filter.Type)) continue;
            var feature = filter.Features.FirstOrDefault();
            if (feature == null || SkipFeatureTypes.Contains(feature.Type)) continue;

            var exists = await db.CategoryFilters.AnyAsync(
                f => f.CategoryId == categoryId && f.SourceFeatureId == feature.Id, stoppingToken);
            if (exists) continue;

            var filterType = filter.Type switch
            {
                "FILTER_TYPE_RANGE" => "range",
                "FILTER_TYPE_OPTIONS" => "select",
                "FILTER_TYPE_FEATURES_AND" => "checkbox",
                _ => "select"
            };

            var key = filter.Title?.Translated?
                          .ToLower().Replace(" ", "_").Replace(".", "")
                      ?? $"feature_{feature.Id}";

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
    public async ValueTask DisposeAsync()
    {
        await _maklerParser.DisposeAsync();
    }
}