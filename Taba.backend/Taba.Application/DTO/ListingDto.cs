namespace Taba.Application.DTO;

public class ListingDto
{
    public int Id { get; set; }
    public string Title { get; set; }// = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; }// = "MDL";
    public string Url { get; set; } //= string.Empty;

    public List<string> Images { get; set; }// = new();

    public string? Category { get; set; }
}