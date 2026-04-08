namespace Taba.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}