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

    public DbSet<Estimate> Estimates => Set<Estimate>();

    public DbSet<EstimateLine> EstimateLines => Set<EstimateLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    public DbSet<StaffInvite> StaffInvites => Set<StaffInvite>();

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

        // v1.3: role, active flag, name and commission percent (the migration backfills existing users as active Owners).
        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            user.Property(u => u.FullName).HasMaxLength(100);
            user.Property(u => u.CommissionPercent).HasPrecision(5, 2);
            user.HasIndex(u => new { u.ShopId, u.Role });
            user.Ignore(u => u.DisplayLabel);
        });

        // A user can only ever see their own shop row.
        builder.Entity<Shop>(shop =>
        {
            shop.HasQueryFilter(s => s.Id == CurrentShopId);
            shop.Property(s => s.AccentColor).HasMaxLength(20);
            shop.Property(s => s.VatPercent).HasPrecision(5, 2);
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
            job.Property(j => j.DiscountAmount).HasPrecision(12, 2);
            job.Property(j => j.DiscountPercent).HasPrecision(5, 2);
            job.Property(j => j.VatPercent).HasPrecision(5, 2);
            job.HasIndex(j => new { j.ShopId, j.Status });
            job.HasIndex(j => new { j.ShopId, j.VehicleId });
            job.HasOne(j => j.Vehicle).WithMany().HasForeignKey(j => j.VehicleId).OnDelete(DeleteBehavior.Restrict);
            job.HasOne<Shop>().WithMany().HasForeignKey(j => j.ShopId).OnDelete(DeleteBehavior.Restrict);
            // The totals are worked out from the lines, never stored.
            job.HasIndex(j => new { j.ShopId, j.InvoiceNumber }).IsUnique();
            job.HasIndex(j => j.EstimateId);
            job.Property(j => j.AssignedToUserId).HasMaxLength(450);
            job.HasIndex(j => new { j.ShopId, j.AssignedToUserId });
            job.HasOne(j => j.AssignedTo).WithMany().HasForeignKey(j => j.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            job.Ignore(j => j.InvoiceLabel);
            job.Ignore(j => j.PartsTotal).Ignore(j => j.LaborTotal).Ignore(j => j.Total).Ignore(j => j.IsLocked)
                .Ignore(j => j.Subtotal).Ignore(j => j.DiscountValue).Ignore(j => j.VatValue).Ignore(j => j.PaidTotal).Ignore(j => j.Balance).Ignore(j => j.PaymentState);
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

        builder.Entity<Estimate>(estimate =>
        {
            estimate.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            estimate.Property(e => e.Notes).HasMaxLength(2000);
            estimate.Property(e => e.DiscountAmount).HasPrecision(12, 2);
            estimate.Property(e => e.DiscountPercent).HasPrecision(5, 2);
            estimate.Property(e => e.VatPercent).HasPrecision(5, 2);
            estimate.HasIndex(e => new { e.ShopId, e.Number }).IsUnique();
            estimate.HasIndex(e => new { e.ShopId, e.Status });
            estimate.HasOne(e => e.Vehicle).WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.Restrict);
            estimate.HasOne<Shop>().WithMany().HasForeignKey(e => e.ShopId).OnDelete(DeleteBehavior.Restrict);
            estimate.Ignore(e => e.Label).Ignore(e => e.Subtotal).Ignore(e => e.DiscountValue).Ignore(e => e.VatValue).Ignore(e => e.Total).Ignore(e => e.IsLocked);
        });

        builder.Entity<EstimateLine>(line =>
        {
            line.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            line.Property(l => l.Description).HasMaxLength(200);
            line.Property(l => l.Quantity).HasPrecision(10, 2);
            line.Property(l => l.UnitPrice).HasPrecision(12, 2);
            line.HasOne(l => l.Estimate).WithMany(e => e.Lines).HasForeignKey(l => l.EstimateId).OnDelete(DeleteBehavior.Cascade);
            line.HasOne<Shop>().WithMany().HasForeignKey(l => l.ShopId).OnDelete(DeleteBehavior.Restrict);
            line.Ignore(l => l.LineTotal);
        });

        builder.Entity<Payment>(payment =>
        {
            payment.Property(p => p.Amount).HasPrecision(12, 2);
            payment.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
            payment.Property(p => p.Note).HasMaxLength(200);
            payment.HasIndex(p => new { p.ShopId, p.JobCardId });
            payment.HasOne(p => p.JobCard).WithMany(j => j.Payments).HasForeignKey(p => p.JobCardId).OnDelete(DeleteBehavior.Cascade);
            payment.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NumberSequence>(sequence =>
        {
            sequence.Property(s => s.Kind).HasMaxLength(30);
            sequence.HasIndex(s => new { s.ShopId, s.Kind }).IsUnique();
            sequence.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ShareLink>(link =>
        {
            link.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
            link.Property(l => l.TokenHash).HasMaxLength(32);
            link.Property(l => l.TokenProtected).HasMaxLength(500);
            link.Property(l => l.Language).HasMaxLength(5);
            link.HasIndex(l => l.TokenHash).IsUnique();
            link.HasIndex(l => new { l.ShopId, l.Kind, l.TargetId });
            link.HasOne<Shop>().WithMany().HasForeignKey(l => l.ShopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StaffInvite>(invite =>
        {
            invite.Property(i => i.TokenHash).HasMaxLength(64);
            invite.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);
            invite.Property(i => i.FullName).HasMaxLength(100);
            invite.Property(i => i.Phone).HasMaxLength(30);
            invite.Property(i => i.CreatedByUserId).HasMaxLength(450);
            invite.Property(i => i.UsedByUserId).HasMaxLength(450);
            invite.HasIndex(i => i.TokenHash).IsUnique();
            invite.HasIndex(i => new { i.ShopId, i.UsedAtUtc, i.RevokedAtUtc });
            invite.HasOne<Shop>().WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Restrict);
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
