using Taba.Application.DTO;

namespace Taba.Application.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryTreeDto>> GetCategoryTreeAsync();
    Task<List<int>> GetAllChildCategoryIdsAsync(int categoryId);
}