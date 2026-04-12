using Microsoft.EntityFrameworkCore;
using Taba.Domain.Entities;

namespace Taba.Infrastucture.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<SourceCountry> SourceCountries => Set<SourceCountry>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();
    public DbSet<ListingCategory> ListingCategories => Set<ListingCategory>();
    public DbSet<ListingPriceHistory> ListingPriceHistories => Set<ListingPriceHistory>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SourceCategoryMapping> SourceCategoryMappings => Set<SourceCategoryMapping>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ListingPromotion> ListingPromotions => Set<ListingPromotion>();
    public DbSet<ListingAttribute> ListingAttributes => Set<ListingAttribute>();
    public DbSet<CategoryFilter> CategoryFilters => Set<CategoryFilter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}