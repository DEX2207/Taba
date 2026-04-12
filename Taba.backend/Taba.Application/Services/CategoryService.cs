using Microsoft.EntityFrameworkCore;
using Taba.Application.DTO;
using Taba.Application.Interfaces;
using Taba.Infrastucture.Persistence;

namespace Taba.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CategoryTreeDto>> GetCategoryTreeAsync()
    {
        var categories = await _db.Categories.ToListAsync();

        var lookup = categories
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key!.Value, g => g.ToList());

        var rootCategories = categories
            .Where(c => c.ParentId == null)
            .ToList();

        List<CategoryTreeDto> BuildTree(int parentId)
        {
            if (!lookup.ContainsKey(parentId))
                return new List<CategoryTreeDto>();

            return lookup[parentId]
                .Select(c => new CategoryTreeDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Children = BuildTree(c.Id)
                })
                .ToList();
        }

        return rootCategories
            .Select(c => new CategoryTreeDto
            {
                Id = c.Id,
                Name = c.Name,
                Children = BuildTree(c.Id)
            })
            .ToList();
    }
    public async Task<List<int>> GetAllChildCategoryIdsAsync(int categoryId)
    {
        var categories = await _db.Categories.ToListAsync();
        
        var lookup = categories
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new List<int>();

        void Collect(int parentId)
        {
            result.Add(parentId);

            if (!lookup.ContainsKey(parentId))
                return;

            foreach (var childId in lookup[parentId])
            {
                Collect(childId);
            }
        }

        Collect(categoryId);

        return result;
    }
    public async Task<List<CategoryFilterDto>> GetCategoryFiltersAsync(int categoryId)
    {
        var allIds = new List<int> { categoryId };

        var category = await _db.Categories.FindAsync(categoryId);
        if (category?.ParentId != null)
            allIds.Add(category.ParentId.Value);

        var filters = await _db.CategoryFilters
            .Where(f => f.CategoryId == categoryId ||
                        (allIds.Contains(f.CategoryId) && f.IsInherited))
            .OrderBy(f => f.SortOrder)
            .ToListAsync();

        return filters.Select(f => new CategoryFilterDto
        {
            Key = f.Key,
            Label = f.Label,
            FilterType = f.FilterType,
            SortOrder = f.SortOrder,
            Options = f.Options != null
                ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(f.Options)
                : null
        }).ToList();
    }
}