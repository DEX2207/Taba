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
}