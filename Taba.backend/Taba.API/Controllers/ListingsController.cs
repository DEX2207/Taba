using Microsoft.AspNetCore.Mvc;
using Taba.Application.DTO;
using Taba.Application.Interfaces;

namespace Taba.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ListingsController : ControllerBase
{
    private readonly IListingService _service;

    public ListingsController(IListingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetListings([FromQuery] ListingFilterDto filter)
    {
        var result = await _service.GetListingsAsync(filter);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var listing = await _service.GetByIdAsync(id);

        if (listing == null)
            return NotFound();

        return Ok(listing);
    }
}