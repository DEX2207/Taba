using Taba.Application.DTO;

namespace Taba.Application.Interfaces;

public interface IListingService
{
    Task<List<ListingDto>> GetListingsAsync(ListingFilterDto filter);
    Task<ListingDto?> GetByIdAsync(int id);
}