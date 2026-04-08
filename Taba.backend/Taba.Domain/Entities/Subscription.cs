namespace Taba.Domain.Entities;

public class Subscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Query { get; set; } = string.Empty;

    public User User { get; set; } = null!;
}