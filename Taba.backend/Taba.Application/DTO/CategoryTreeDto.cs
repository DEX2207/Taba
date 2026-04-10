namespace Taba.Application.DTO;

public class CategoryTreeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<CategoryTreeDto> Children { get; set; } = new();
}