using System.Reflection;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Data;

public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    private static readonly MethodInfo ApplyShopFilterMethod = typeof(ApplicationDbContext)
        .GetMethod(nameof(ApplyShopFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ICurrentShop _currentShop;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentShop currentShop)
        : base(options)
    {
        _currentShop = currentShop;
    }

    // Lets a test context that derives from this one pass its own options type.
    protected ApplicationDbContext(DbContextOptions options, ICurrentShop currentShop)
        : base(options)
    {
        _currentShop = currentShop;
    }

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<ShopLogo> ShopLogos => Set<ShopLogo>();

    // Read by the query filters below. EF evaluates it per context instance, i.e. per request.
    private int? CurrentShopId => _currentShop.ShopId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>()
            .HasOne(u => u.Shop)
            .WithMany()
            .HasForeignKey(u => u.ShopId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user can only ever see their own shop row.
        builder.Entity<Shop>().HasQueryFilter(s => s.Id == CurrentShopId);

        // At most one logo per shop. (The tenant query filter comes from IShopOwned, below.)
        builder.Entity<ShopLogo>(logo =>
        {
            logo.HasIndex(l => l.ShopId).IsUnique();
            logo.Property(l => l.ContentType).HasMaxLength(50);
            logo.HasOne<Shop>().WithMany().HasForeignKey(l => l.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        // THE PATTERN: every entity that implements IShopOwned gets the same filter automatically.
        // Adding a new shop-owned entity later needs no extra filter code, just ": IShopOwned".
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(IShopOwned).IsAssignableFrom(t.ClrType)))
        {
            ApplyShopFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    // If CurrentShopId is null (nobody signed in / no shop yet) this compares ShopId to NULL,
    // which matches nothing: the filter fails closed.
    private void ApplyShopFilter<TEntity>(ModelBuilder builder) where TEntity : class, IShopOwned =>
        builder.Entity<TEntity>().HasQueryFilter(e => e.ShopId == CurrentShopId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceShopOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceShopOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>New shop-owned rows get the current shop's id; writing into another shop is refused.</summary>
    private void EnforceShopOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<IShopOwned>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var shopId = CurrentShopId
                         ?? throw new InvalidOperationException("Cannot save shop data without a current shop.");

            if (entry.State == EntityState.Added && entry.Entity.ShopId == 0)
            {
                entry.Entity.ShopId = shopId;
            }
            else if (entry.Entity.ShopId != shopId)
            {
                throw new InvalidOperationException("Cannot save data that belongs to another shop.");
            }
        }
    }
}
