using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Taba.Parser.Models.Makler;

namespace Taba.Parser.Parsers;

/// <summary>
/// Парсер объявлений с makler.md.
/// Использует Playwright (реальный Chromium) для обхода Cloudflare защиты.
/// </summary>
public class MaklerParser : IAsyncDisposable
{
    private readonly ILogger<MaklerParser> _logger;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    private bool _initialized = false;

    private const string BaseUrl = "https://makler.md";

    // Slugи которые пропускаем — служебные страницы или ссылки на другие разделы
    private static readonly HashSet<string> SkipSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "an", "an/web", "an/search", "an/notepad",
        "confiscated-property",
        "transnistria"
    };

    public MaklerParser(ILogger<MaklerParser> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Инициализация браузера
    // ─────────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        _logger.LogInformation("MaklerParser: запускаем Chromium...");

        _playwright = await Playwright.CreateAsync();

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
            }
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36",
            Locale = "ru-RU",
            TimezoneId = "Europe/Chisinau",
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"
            }
        });

        await _context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });
            window.chrome = { runtime: {} };
        ");

        await WarmUpAsync();

        _initialized = true;
        _logger.LogInformation("MaklerParser: Chromium готов");
    }

    private async Task WarmUpAsync()
    {
        _logger.LogInformation("MaklerParser: прогрев — открываем главную страницу...");

        var page = await _context!.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/ru/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });

            _logger.LogInformation("MaklerParser: прогрев завершён, URL={Url}", page.Url);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MaklerParser: ошибка прогрева");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Парсинг дерева категорий
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Загружает /ru/categories и возвращает все категории (листовые и промежуточные).
    /// Структура страницы:
    ///   h2.tub > a              — раздел (Недвижимость, Транспорт...)
    ///   div.main > ul.rub > li  — рубрика
    ///     ul.sub > li > a       — подрубрика (если есть)
    /// </summary>
    public async Task<List<MaklerCategory>> FetchCategoryTreeAsync()
    {
        if (!_initialized)
            await InitializeAsync();

        _logger.LogInformation("MaklerParser: загружаем дерево категорий...");

        var html = await LoadHtmlAsync($"{BaseUrl}/ru/categories");
        if (html == null)
        {
            _logger.LogError("MaklerParser: не удалось загрузить страницу категорий");
            return new List<MaklerCategory>();
        }

        var categories = ParseCategoryTree(html);
        _logger.LogInformation("MaklerParser: найдено {Count} категорий", categories.Count);
        return categories;
    }

    private List<MaklerCategory> ParseCategoryTree(HtmlDocument html)
    {
        var result = new List<MaklerCategory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var doc = html.DocumentNode;
        
        var allH2 = doc.SelectNodes("//h2");
        _logger.LogInformation("MaklerParser: всего h2 на странице: {Count}", allH2?.Count ?? 0);
    
        var tubH2 = doc.SelectNodes("//h2[contains(@class,'tub')]");
        _logger.LogInformation("MaklerParser: h2.tub найдено: {Count}", tubH2?.Count ?? 0);
    
        // Первые 500 символов HTML для проверки
        var htmlPreview = html.DocumentNode.InnerHtml;
        htmlPreview = htmlPreview.Length > 500 ? htmlPreview[..500] : htmlPreview;
        _logger.LogInformation("MaklerParser: начало HTML: {Html}", htmlPreview);

        // Каждый раздел — h2.tub > a
        var sectionHeaders = doc.SelectNodes("//h2[contains(@class,'tub')]/a[@href]");
        _logger.LogInformation("MaklerParser: sectionHeaders найдено: {Count}", sectionHeaders?.Count ?? 0);

        if (sectionHeaders != null && sectionHeaders.Count > 0)
        {
            var first = sectionHeaders[0];
            _logger.LogInformation("MaklerParser: первый sectionLink href={Href}, text={Text}",
                first.GetAttributeValue("href", ""),
                first.InnerText.Trim());
    
            // Смотрим что идёт после h2
            var parent = first.ParentNode; // это сам h2
            var next = parent?.NextSibling;
            int steps = 0;
            while (next != null && steps < 5)
            {
                _logger.LogInformation("MaklerParser: sibling[{I}] name={Name} class={Class}",
                    steps, next.Name, next.GetAttributeValue("class", ""));
                next = next.NextSibling;
                steps++;
            }
        }
        if (sectionHeaders == null) return result;

        foreach (var sectionLink in sectionHeaders)
        {
            var sectionHref = sectionLink.GetAttributeValue("href", "");
            var sectionSlug = ExtractSlug(sectionHref);
            var sectionName = HtmlEntity.DeEntitize(sectionLink.InnerText).Trim();

            if (string.IsNullOrEmpty(sectionSlug) || ShouldSkip(sectionSlug)) continue;

            // ✅ h2 — это ParentNode от a, ищем следующий div.main у h2
            var h2Node = sectionLink.ParentNode; // сам <h2>
    
            // Ищем следующий sibling который является div
            var sectionBlock = h2Node.NextSibling;
            while (sectionBlock != null && sectionBlock.Name != "div")
                sectionBlock = sectionBlock.NextSibling;
            
            _logger.LogInformation("MaklerParser: sectionBlock для {Slug}: {Found}, rubrics={Count}",
                sectionSlug,
                sectionBlock?.GetAttributeValue("class", "none") ?? "null",
                sectionBlock?.SelectNodes(".//ul[contains(@class,'rub')]/li")?.Count ?? 0);

            if (sectionBlock == null) continue;
            // ul.rub > li — рубрики
            var rubricItems = sectionBlock.SelectNodes(".//ul[contains(@class,'rub')]/li");
            if (rubricItems == null) continue;

            foreach (var rubricLi in rubricItems)
            {
                var rubricLink = rubricLi.SelectSingleNode("./a[@href]");
                if (rubricLink == null) continue;

                // Пропускаем ссылки-стрелки → на другие разделы
                if (rubricLink.SelectSingleNode(".//div[contains(@class,'label-ascii-arrow')]") != null)
                    continue;

                var rubricHref = rubricLink.GetAttributeValue("href", "");
                var rubricSlug = ExtractSlug(rubricHref);
                var rubricName = HtmlEntity.DeEntitize(rubricLink.InnerText).Trim();

                if (string.IsNullOrEmpty(rubricSlug) || ShouldSkip(rubricSlug)) continue;

                // Подрубрики ul.sub
                var subLinks = rubricLi.SelectNodes(".//ul[contains(@class,'sub')]/li/a[@href]");

                if (subLinks != null && subLinks.Count > 0)
                {
                    // Добавляем рубрику как родительскую категорию
                    TryAdd(result, seen, new MaklerCategory
                    {
                        Slug = rubricSlug,
                        Name = rubricName,
                        ParentSlug = sectionSlug,
                        SectionSlug = sectionSlug,
                        SectionName = sectionName,
                        IsParent = true
                    });

                    // Добавляем подрубрики как листовые категории
                    foreach (var subLink in subLinks)
                    {
                        // Пропускаем стрелки →
                        if (subLink.SelectSingleNode(".//div[contains(@class,'label-ascii-arrow')]") != null)
                            continue;

                        var subHref = subLink.GetAttributeValue("href", "");
                        var subSlug = ExtractSlug(subHref);
                        var subName = HtmlEntity.DeEntitize(subLink.InnerText).Trim();

                        if (string.IsNullOrEmpty(subSlug) || ShouldSkip(subSlug)) continue;

                        TryAdd(result, seen, new MaklerCategory
                        {
                            Slug = subSlug,
                            Name = subName,
                            ParentSlug = rubricSlug,
                            SectionSlug = sectionSlug,
                            SectionName = sectionName,
                            IsParent = false
                        });
                    }
                }
                else
                {
                    // Нет подрубрик — сама рубрика является листом
                    TryAdd(result, seen, new MaklerCategory
                    {
                        Slug = rubricSlug,
                        Name = rubricName,
                        ParentSlug = sectionSlug,
                        SectionSlug = sectionSlug,
                        SectionName = sectionName,
                        IsParent = false
                    });
                }
            }
        }

        return result;
    }

    private static void TryAdd(List<MaklerCategory> list, HashSet<string> seen, MaklerCategory cat)
    {
        if (seen.Add(cat.Slug))
            list.Add(cat);
    }

    private static string ExtractSlug(string href)
    {
        if (string.IsNullOrEmpty(href)) return string.Empty;
        var clean = href.Split('#')[0].Split('?')[0].TrimEnd('/');
        var match = Regex.Match(clean, @"^/r[ou]/(.+)$");
        if (!match.Success) return string.Empty;
    
        var slug = match.Groups[1].Value;
    
        // ✅ Убираем региональные префиксы
        slug = Regex.Replace(slug, @"^(transnistria|chisinau|balti)/", "");
    
        return slug;
    }

    private static bool ShouldSkip(string slug)
    {
        foreach (var skip in SkipSlugs)
            if (slug.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Публичные методы парсинга объявлений
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<MaklerListingItem>> FetchListingPageAsync(string categorySlug, int page = 0)
    {
        var url = page == 0
            ? $"{BaseUrl}/ru/{categorySlug}"
            : $"{BaseUrl}/ru/{categorySlug}?page={page}";

        var html = await LoadHtmlAsync(url);
        if (html == null) return new List<MaklerListingItem>();

        return ParseListingPage(html, categorySlug);
    }

    public async Task<MaklerListingDetail?> FetchListingDetailAsync(string listingUrl)
    {
        var html = await LoadHtmlAsync(listingUrl);
        if (html == null) return null;

        return ParseDetailPage(html, listingUrl);
    }

    public string BuildListingUrl(string categorySlug, string externalId)
        => $"{BaseUrl}/ru/{categorySlug}/an/{externalId}";

    // ─────────────────────────────────────────────────────────────────────────
    // Загрузка HTML через Playwright
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HtmlDocument?> LoadHtmlAsync(string url)
    {
        if (!_initialized)
            await InitializeAsync();

        IPage? page = null;
        try
        {
            page = await _context!.NewPageAsync();

            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });

            if (response == null || !response.Ok)
            {
                _logger.LogError("MaklerParser: HTTP {Status} при загрузке {Url}",
                    response?.Status, url);
                return null;
            }

            await page.WaitForTimeoutAsync(1500);

            var html = await page.ContentAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            _logger.LogDebug("MaklerParser: загружено {Url}", url);
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MaklerParser: ошибка загрузки {Url}", url);
            return null;
        }
        finally
        {
            if (page != null)
                await page.CloseAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Парсинг списка объявлений
    // ─────────────────────────────────────────────────────────────────────────

    private List<MaklerListingItem> ParseListingPage(HtmlDocument html, string categorySlug)
    {
        var result = new List<MaklerListingItem>();
        var doc = html.DocumentNode;

        var articles = doc.SelectNodes("//article[.//a[contains(@href,'/an/')]]");
        if (articles == null || articles.Count == 0)
        {
            _logger.LogWarning("MaklerParser: объявления не найдены на странице категории {Slug}",
                categorySlug);
            return result;
        }

        foreach (var article in articles)
        {
            try
            {
                var item = ParseListingCard(article, categorySlug);
                if (item != null)
                    result.Add(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MaklerParser: ошибка парсинга карточки");
            }
        }

        _logger.LogInformation("MaklerParser: найдено {Count} объявлений в категории {Slug}",
            result.Count, categorySlug);

        return result;
    }

    private MaklerListingItem? ParseListingCard(HtmlNode article, string categorySlug)
    {
        var linkNode = article.SelectSingleNode(
            ".//h3[contains(@class,'ls-detail_antTitle')]//a[contains(@href,'/an/')]" +
            " | .//h3//a[contains(@href,'/an/')]");

        if (linkNode == null) return null;

        var href = linkNode.GetAttributeValue("href", "");
        var id = ExtractAdId(href);
        if (string.IsNullOrEmpty(id)) return null;

        var cleanHref = href.Split('?')[0]; // убираем ?top и прочие параметры
        var listingUrl = cleanHref.StartsWith("http")
            ? cleanHref
            : $"{BaseUrl}{cleanHref}";

        var title = linkNode.GetAttributeValue("title", "").Trim();
        if (string.IsNullOrEmpty(title))
            title = HtmlEntity.DeEntitize(linkNode.InnerText).Trim();

        var priceNode = article.SelectSingleNode(
            ".//span[contains(@class,'ls-detail_price')] | .//div[contains(@class,'priceBox')]//span");
        var (price, currency) = ParsePrice(
            priceNode != null ? HtmlEntity.DeEntitize(priceNode.InnerText).Trim() : null);

        var isTop = href.Contains("?top") ||
                    article.GetAttributeValue("class", "").Contains("top");

        var imgNode = article.SelectSingleNode(".//img[@src]");
        var imgSrc = imgNode?.GetAttributeValue("src", "");
        if (imgSrc?.Contains("placeholder") == true) imgSrc = null;

        var dateNode = article.SelectSingleNode(".//span[contains(@class,'ls-detail_time')]");
        var dateRaw = dateNode != null
            ? HtmlEntity.DeEntitize(dateNode.InnerText).Trim()
            : "";

        return new MaklerListingItem
        {
            Id = id,
            Title = title,
            Price = price,
            Currency = currency,
            City = "",
            DateRaw = dateRaw,
            PreviewImageUrl = imgSrc,
            Url = listingUrl,
            IsTop = isTop
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Парсинг детальной страницы
    // ─────────────────────────────────────────────────────────────────────────

    private MaklerListingDetail? ParseDetailPage(HtmlDocument html, string url)
    {
        var doc = html.DocumentNode;

        var id = ExtractAdId(url);
        if (string.IsNullOrEmpty(id)) return null;

        var titleNode = doc.SelectSingleNode("//h1//strong[@id='anNameData']");
        var title = titleNode != null
            ? HtmlEntity.DeEntitize(titleNode.InnerText).Trim()
            : string.Empty;

        var priceNode = doc.SelectSingleNode(
            "//div[contains(@class,'item_title_price')] | //div[contains(@class,'user-price')]");
        var (price, currency) = ParsePrice(
            priceNode != null ? HtmlEntity.DeEntitize(priceNode.InnerText).Trim() : null);

        var cityNode = doc.SelectSingleNode("//ul[contains(@class,'item-city')]/li");
        var city = cityNode != null
            ? HtmlEntity.DeEntitize(cityNode.InnerText).Trim()
            : string.Empty;

        if (string.IsNullOrEmpty(city))
        {
            var titleInfoSpan = doc.SelectSingleNode(
                "//div[contains(@class,'item_title_info')]/span[1]");
            if (titleInfoSpan != null)
                city = HtmlEntity.DeEntitize(titleInfoSpan.InnerText).Trim();
        }

        var descNode = doc.SelectSingleNode(
            "//div[@id='anText'] | //div[contains(@class,'ittext')]");
        var description = descNode != null
            ? HtmlEntity.DeEntitize(descNode.InnerText).Trim()
            : null;

        var imageNodes = doc.SelectNodes(
            "//a[contains(@href,'media.makler.md') and contains(@href,'/original/')]");
        var images = new List<string>();
        if (imageNodes != null)
        {
            foreach (var imgLink in imageNodes)
            {
                var imgHref = imgLink.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(imgHref) && !images.Contains(imgHref))
                    images.Add(imgHref);
            }
        }

        var phoneNode = doc.SelectSingleNode(
            "//ul[@id='item_phones']//li[@itemprop='telephone']");
        var phone = phoneNode != null
            ? HtmlEntity.DeEntitize(phoneNode.InnerText).Trim()
            : null;

        var sellerNode = doc.SelectSingleNode("//a[contains(@href,'/an/user/index/id/')]");
        var sellerName = sellerNode != null
            ? HtmlEntity.DeEntitize(sellerNode.InnerText).Trim()
            : null;
        var sellerHref = sellerNode?.GetAttributeValue("href", "") ?? "";
        var externalSellerId = Regex.Match(sellerHref, @"/id/(\d+)").Groups[1].Value;
        if (string.IsNullOrEmpty(externalSellerId)) externalSellerId = null;

        var attributes = ParseAttributes(doc);
        attributes.TryGetValue("Sectorul", out var district);

        return new MaklerListingDetail
        {
            Id = id,
            Title = title,
            Price = price,
            Currency = currency,
            City = city,
            District = district,
            Description = description,
            DateRaw = "",
            Url = url,
            ImageUrls = images,
            Phone = phone,
            SellerName = sellerName,
            ExternalSellerId = externalSellerId,
            Attributes = attributes
        };
    }

    private Dictionary<string, string> ParseAttributes(HtmlNode doc)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var rows = doc.SelectNodes("//ul[contains(@class,'itemtable')]//li");
        if (rows == null) return result;

        foreach (var li in rows)
        {
            var keyNode = li.SelectSingleNode(".//div[contains(@class,'fields')]");
            var valNode = li.SelectSingleNode(".//div[contains(@class,'values')]");

            if (keyNode == null || valNode == null) continue;

            var key = HtmlEntity.DeEntitize(keyNode.InnerText).Trim().TrimEnd(':');
            var value = HtmlEntity.DeEntitize(valNode.InnerText).Trim();

            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                result[key] = value;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    private static string ExtractAdId(string href)
    {
        var match = Regex.Match(href, @"/an/(\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    public static (decimal? Price, string Currency) ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "MDL");

        var currency = "MDL";
        if (text.Contains("USD") || text.Contains("$")) currency = "USD";
        else if (text.Contains("EUR") || text.Contains("€")) currency = "EUR";
        else if (text.Contains("MDL") || text.Contains("Lei") || text.Contains("lei"))
            currency = "MDL";

        var digits = Regex.Replace(text, @"[^\d]", "");
        if (string.IsNullOrEmpty(digits)) return (null, currency);

        if (decimal.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            return (price, currency);

        return (null, currency);
    }
    
    // ─────────────────────────────────────────────────────────────────────────
    // Освобождение ресурсов
    // ─────────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_context != null) await _context.DisposeAsync();
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}