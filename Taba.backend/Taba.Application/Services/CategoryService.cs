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
            .GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<CategoryTreeDto> BuildTree(int? parentId)
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

        return BuildTree(null);
    }
}