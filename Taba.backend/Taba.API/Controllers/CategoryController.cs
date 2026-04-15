using Microsoft.AspNetCore.Mvc;
using Taba.Application.Interfaces;

namespace Taba.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoryController:ControllerBase
{
    private readonly ICategoryService _service;
    
    public CategoryController(ICategoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategoryTree()
    {
        var result = await _service.GetCategoryTreeAsync();
        return Ok(result);
    }
    [HttpGet("{id}/filters")]
    public async Task<IActionResult> GetFilters(int id)
    {
        var filters = await _service.GetCategoryFiltersAsync(id);
        return Ok(filters);
    }
}