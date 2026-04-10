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

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(l => l.Title.Contains(filter.Search));

        var listings = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(l => new ListingDto
            {
                Id = l.Id,
                Title = l.Title,
                Price = l.Price,
                Currency = l.Currency,
                Url = l.Url,
                Images = l.Images.Select(i => i.Url).ToList()
            })
            .ToListAsync();

        return listings;
    }

    public async Task<ListingDto?> GetByIdAsync(int id)
    {
        return await _db.Listings
            .Where(l => l.Id == id)
            .Select(l => new ListingDto
            {
                Id = l.Id,
                Title = l.Title,
                Price = l.Price,
                Currency = l.Currency,
                Url = l.Url,
                Images = l.Images.Select(i => i.Url).ToList()
            })
            .FirstOrDefaultAsync();
    }
}