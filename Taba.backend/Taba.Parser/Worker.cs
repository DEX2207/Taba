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

        var leafCategories = _parser.GetLeafCategories(categoryTree)
            .Where(c => c.Type == "CATEGORY") // только реальные категории
            .Take(20) // для теста берём 20
            .ToList();
        _logger.LogInformation("Найдено {Count} конечных категорий", leafCategories.Count);

        foreach (var category in leafCategories)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation("Парсим категорию {Id} — {Name}",
                category.Id, category.Title?.Translated);

            await ParseCategoryAsync(db, source, category.Id, stoppingToken);

            // Задержка между категориями
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ParseCategoryAsync(
        AppDbContext db,
        Source source,
        int subCategoryId,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Парсим категорию {SubCategoryId}", subCategoryId);

        int skip = 0;
        const int limit = 78;
        const int maxPages = 3; // максимум 234 объявления на категорию
        int pageCount = 0;
        int totalParsed = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (pageCount >= maxPages) break;

            var ads = await _parser.FetchAdsAsync(subCategoryId, limit, skip);

            if (ads.Count == 0) break;

            foreach (var ad in ads)
                await ProcessAdAsync(db, source, ad, stoppingToken);

            await db.SaveChangesAsync(stoppingToken);
            totalParsed += ads.Count;
            pageCount++;

            _logger.LogInformation("Категория {SubCategoryId}: страница {Page}, обработано {Count}",
                subCategoryId, pageCount, totalParsed);

            if (ads.Count < limit) break;
            skip += limit;

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }

    private async Task ProcessAdAsync(
        AppDbContext db,
        Source source,
        Ad ad,
        CancellationToken stoppingToken)
    {
        // Проверяем существует ли объявление
        var existing = await db.Listings.FirstOrDefaultAsync(
            l => l.SourceId == source.Id && l.ExternalId == ad.Id,
            stoppingToken);

        var (price, currency) = _parser.ExtractPrice(ad);
        var imageUrls = _parser.BuildImageUrls(ad);
        var url = _parser.BuildAdUrl(ad.Id);

        if (existing != null)
        {
            // Обновляем существующее объявление
            if (existing.Price != price)
            {
                // Сохраняем историю цены
                db.ListingPriceHistories.Add(new ListingPriceHistory
                {
                    ListingId = existing.Id,
                    Price = price,
                    Currency = currency,
                    RecordedAt = DateTime.UtcNow
                });
            }
            // Обновляем категорию если её ещё нет
            var categoryId = await ResolveListingCategoryAsync(db, source, ad.SubCategory, stoppingToken);
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

            existing.Price = price;
            existing.Currency = currency;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ParsedAt = DateTime.UtcNow;
            existing.Status = ListingStatus.Active;
        }
        else
        {
            // Создаём новое объявление
            // Пока используем заглушки для CountryId (1 = Молдова)
            var listing = new Listing
            {
                SourceId = source.Id,
                ExternalId = ad.Id,
                Title = ad.Title ?? string.Empty,
                Price = price,
                Currency = currency,
                Url = url,
                Status = ListingStatus.Active,
                CountryId = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ParsedAt = DateTime.UtcNow
            };

            db.Listings.Add(listing);
            await db.SaveChangesAsync(stoppingToken);
            // Привязываем категорию
            var categoryId = await ResolveListingCategoryAsync(db, source, ad.SubCategory, stoppingToken);
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

            // Добавляем изображения
            for (int i = 0; i < imageUrls.Count; i++)
            {
                db.ListingImages.Add(new ListingImage
                {
                    ListingId = listing.Id,
                    Url = imageUrls[i],
                    OrderIndex = i
                });
            }

            // Начальная запись в историю цен
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
}