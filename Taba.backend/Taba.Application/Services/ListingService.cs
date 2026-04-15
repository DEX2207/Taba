using Microsoft.EntityFrameworkCore;
using Taba.Application.DTO;
using Taba.Application.Interfaces;
using Taba.Domain.Enums;
using Taba.Infrastucture.Persistence;

namespace Taba.Application.Services;

public class ListingService : IListingService
{
    private readonly AppDbContext _db;
    
    private readonly ICategoryService _categoryService;

    public ListingService(AppDbContext db, ICategoryService categoryService)
    {
        _db = db;
        _categoryService = categoryService;
    }

    public async Task<List<ListingDto>> GetListingsAsync(ListingFilterDto filter)
    {
        var query = _db.Listings
            .AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active)
            .AsQueryable();
        
        
        if (filter.CategoryId.HasValue)
        {
            var categoryIds = await _categoryService
                .GetAllChildCategoryIdsAsync(filter.CategoryId.Value);

            query = query.Where(l =>
                _db.ListingCategories
                    .Any(lc => lc.ListingId == l.Id && categoryIds.Contains(lc.CategoryId))
            );
        }

        if (filter.MinPrice.HasValue)
            query = query.Where(l => l.Price >= filter.MinPrice);

        if (filter.MaxPrice.HasValue)
            query = query.Where(l => l.Price <= filter.MaxPrice);
        
        //Фильтрация по аттрибутам
        foreach (var (key, value) in filter.Attrs)
        {
            var k = key;
            var v = value;
            query = query.Where(l =>
                l.Attributes.Any(a => a.Key == k && a.Value == v));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(l => l.Title.Contains(filter.Search));
        
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var rawListings = await query
            .OrderBy(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.Title,
                l.Description,
                l.Price,
                l.Currency,
                l.Url,
                l.RawRegionName,
                Images = l.Images.Select(i => i.Url).ToList(),
                Attributes = l.Attributes.Select(a => new { a.Key, a.Value }).ToList()
            })
            .ToListAsync();

        return rawListings.Select(l => new ListingDto
        {
            Id = l.Id,
            Title = l.Title,
            Description = l.Description,
            Price = l.Price,
            Currency = l.Currency,
            Url = l.Url,
            RegionName = l.RawRegionName,
            Images = l.Images.ToList(),
            Attributes = l.Attributes.ToDictionary(a => a.Key, a => a.Value)
        }).ToList();
    }

    public async Task<ListingDto?> GetByIdAsync(int id)
    {
        var listing = await _db.Listings
            .Where(l => l.Id == id)
            .Select(l => new
            {
                l.Id,
                l.Title,
                l.Description,
                l.Price,
                l.Currency,
                l.Url,
                l.RawRegionName,
                Images = l.Images.Select(i => i.Url).ToList(),
                Attributes = l.Attributes.Select(a => new { a.Key, a.Value }).ToList()
            })
            .FirstOrDefaultAsync();

        if (listing == null) return null;

        return new ListingDto
        {
            Id = listing.Id,
            Title = listing.Title,
            Description = listing.Description,
            Price = listing.Price,
            Currency = listing.Currency,
            Url = listing.Url,
            RegionName = listing.RawRegionName,
            Images = listing.Images,
            Attributes = listing.Attributes.ToDictionary(a => a.Key, a => a.Value)
        };
    }
}