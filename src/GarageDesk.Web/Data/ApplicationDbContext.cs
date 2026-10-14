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

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<JobCard> JobCards => Set<JobCard>();

    public DbSet<JobLine> JobLines => Set<JobLine>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

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
        builder.Entity<Shop>(shop =>
        {
            shop.HasQueryFilter(s => s.Id == CurrentShopId);
            shop.Property(s => s.AccentColor).HasMaxLength(20);
        });

        // At most one logo per shop. (The tenant query filter comes from IShopOwned, below.)
        builder.Entity<ShopLogo>(logo =>
        {
            logo.HasIndex(l => l.ShopId).IsUnique();
            logo.Property(l => l.ContentType).HasMaxLength(50);
            logo.HasOne<Shop>().WithMany().HasForeignKey(l => l.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.Name).HasMaxLength(100);
            customer.Property(c => c.Phone).HasMaxLength(20);
            customer.Property(c => c.Notes).HasMaxLength(1000);
            customer.HasIndex(c => new { c.ShopId, c.Phone });
            customer.HasOne<Shop>().WithMany().HasForeignKey(c => c.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Vehicle>(vehicle =>
        {
            vehicle.Property(v => v.Plate).HasMaxLength(20);
            vehicle.Property(v => v.PlateKey).HasMaxLength(20);
            vehicle.Property(v => v.Vin).HasMaxLength(17);
            vehicle.Property(v => v.Make).HasMaxLength(50);
            vehicle.Property(v => v.Model).HasMaxLength(50);
            vehicle.HasIndex(v => new { v.ShopId, v.PlateKey });
            vehicle.HasOne(v => v.Customer).WithMany(c => c.Vehicles).HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
            vehicle.HasOne<Shop>().WithMany().HasForeignKey(v => v.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobCard>(job =>
        {
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.Notes).HasMaxLength(2000);
            job.HasIndex(j => new { j.ShopId, j.Status });
            job.HasIndex(j => new { j.ShopId, j.VehicleId });
            job.HasOne(j => j.Vehicle).WithMany().HasForeignKey(j => j.VehicleId).OnDelete(DeleteBehavior.Restrict);
            job.HasOne<Shop>().WithMany().HasForeignKey(j => j.ShopId).OnDelete(DeleteBehavior.Restrict);
            // The totals are worked out from the lines, never stored.
            job.Ignore(j => j.PartsTotal).Ignore(j => j.LaborTotal).Ignore(j => j.Total).Ignore(j => j.IsLocked);
        });

        builder.Entity<JobLine>(line =>
        {
            line.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            line.Property(l => l.Description).HasMaxLength(200);
            line.Property(l => l.Quantity).HasPrecision(10, 2);
            line.Property(l => l.UnitPrice).HasPrecision(12, 2);
            line.HasOne(l => l.JobCard).WithMany(j => j.Lines).HasForeignKey(l => l.JobCardId).OnDelete(DeleteBehavior.Cascade);
            line.HasOne<Shop>().WithMany().HasForeignKey(l => l.ShopId).OnDelete(DeleteBehavior.Restrict);
            line.Ignore(l => l.LineTotal);
        });

        builder.Entity<Reminder>(reminder =>
        {
            reminder.Property(r => r.Type).HasMaxLength(60);
            reminder.HasIndex(r => new { r.ShopId, r.IsDone, r.DueDate });
            reminder.HasOne(r => r.Vehicle).WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Restrict);
            reminder.HasOne<Shop>().WithMany().HasForeignKey(r => r.ShopId).OnDelete(DeleteBehavior.Restrict);
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
