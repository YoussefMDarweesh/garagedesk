using System.Security.Cryptography;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

public sealed record PublicLine(string Description, JobLineType Type, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// Everything a customer is allowed to see, and nothing else. The public pages render this, never an entity,
/// so an internal field (notes, ids, staff, payment notes) cannot leak by accident: it is simply not here.
/// </summary>
public sealed class PublicShareView
{
    public ShareLinkKind Kind { get; init; }

    public string ShopName { get; init; } = "";

    public string ShopPhone { get; init; } = "";

    public string ShopAddress { get; init; } = "";

    public string? ShopAccent { get; init; }

    public bool HasLogo { get; init; }

    public string CustomerName { get; init; } = "";

    public string VehicleTitle { get; init; } = "";

    public int? VehicleYear { get; init; }

    public string Plate { get; init; } = "";

    /// <summary>INV-0001 or EST-0001; null for a job card that has not been issued as an invoice yet.</summary>
    public string? Label { get; init; }

    public DateOnly Date { get; init; }

    public DateOnly? ValidUntil { get; init; }

    public List<PublicLine> Lines { get; init; } = [];

    public decimal Parts { get; init; }

    public decimal Labor { get; init; }

    public decimal Subtotal { get; init; }

    public decimal Discount { get; init; }

    public decimal? DiscountPercent { get; init; }

    public decimal Vat { get; init; }

    public decimal? VatPercent { get; init; }

    public decimal Total { get; init; }

    public decimal Received { get; init; }

    public decimal Balance { get; init; }

    public PaymentState PaymentState { get; init; }

    public JobStatus? Status { get; init; }

    public DateOnly? LastUpdated { get; init; }
}

/// <summary>
/// The customer's side of share links. A request has no login and no shop, so this is the ONE place that
/// reads across the tenant filter, and it does so under strict rules:
/// the link is found only by the hash of its secret token; its own ShopId is then the only shop that is read; and
/// every query repeats "ShopId == link.ShopId", so even a tampered TargetId can never reach another shop's data.
/// </summary>
public class PublicShareLoader(ApplicationDbContext db)
{
    /// <summary>The live link for a token, or null. Unknown, expired and revoked all give the same null.</summary>
    public async Task<ShareLink?> FindLiveLinkAsync(string? token, CancellationToken ct = default)
    {
        if (!ShareTokens.IsWellFormed(token))
        {
            return null;
        }

        var hash = ShareTokens.Hash(token!);
        var link = await db.ShareLinks.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == hash, ct);

        // The database already matched on the hash; compare again in constant time rather than trust the query alone.
        if (link is null || !CryptographicOperations.FixedTimeEquals(link.TokenHash, hash))
        {
            return null;
        }

        return link.IsLive(DateTime.UtcNow) ? link : null;
    }

    /// <summary>Counts a view. A single UPDATE, so concurrent views cannot lose a count and no tenant-scoped save is needed.</summary>
    public Task RecordViewAsync(int linkId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.ShareLinks.IgnoreQueryFilters()
            .Where(l => l.Id == linkId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.ViewCount, l => l.ViewCount + 1)
                .SetProperty(l => l.LastViewedAtUtc, now), ct);
    }

    public async Task<(byte[] Bytes, string ContentType)?> LoadLogoAsync(ShareLink link, CancellationToken ct = default)
    {
        var logo = await db.ShopLogos.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.ShopId == link.ShopId)
            .Select(l => new { l.Bytes, l.ContentType })
            .FirstOrDefaultAsync(ct);
        return logo is null ? null : (logo.Bytes, logo.ContentType);
    }

    /// <summary>The page content for a link, or null when its target no longer exists in the link's own shop.</summary>
    public async Task<PublicShareView?> LoadAsync(ShareLink link, CancellationToken ct = default)
    {
        var shop = await db.Shops.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == link.ShopId, ct);
        if (shop is null)
        {
            return null;
        }

        var hasLogo = await db.ShopLogos.IgnoreQueryFilters().AnyAsync(l => l.ShopId == link.ShopId, ct);

        return link.Kind == ShareLinkKind.Estimate
            ? await LoadEstimateAsync(link, shop, hasLogo, ct)
            : await LoadJobAsync(link, shop, hasLogo, ct);
    }

    private async Task<PublicShareView?> LoadJobAsync(ShareLink link, Shop shop, bool hasLogo, CancellationToken ct)
    {
        var shopId = link.ShopId;
        var job = await db.JobCards.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == link.TargetId && j.ShopId == shopId, ct);
        if (job is null)
        {
            return null;
        }

        var vehicle = await db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == job.VehicleId && v.ShopId == shopId, ct);
        if (vehicle is null)
        {
            return null;
        }

        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == vehicle.CustomerId && c.ShopId == shopId, ct);
        if (customer is null)
        {
            return null;
        }

        // The totals come from the same JobCard code the owner's pages use, so the two can never disagree.
        job.Lines = await db.JobLines.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.JobCardId == job.Id && l.ShopId == shopId).OrderBy(l => l.Id).ToListAsync(ct);
        job.Payments = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.JobCardId == job.Id && p.ShopId == shopId).ToListAsync(ct);

        var showMoney = link.Kind == ShareLinkKind.Invoice;
        return new PublicShareView
        {
            Kind = link.Kind,
            ShopName = shop.Name,
            ShopPhone = shop.Phone,
            ShopAddress = shop.Address,
            ShopAccent = shop.AccentColor,
            HasLogo = hasLogo,
            CustomerName = customer.Name,
            VehicleTitle = $"{vehicle.Make} {vehicle.Model}".Trim(),
            VehicleYear = vehicle.Year,
            Plate = vehicle.Plate,
            Label = job.InvoiceLabel,
            Date = job.DateIn,
            Status = job.Status,
            LastUpdated = job.StatusChangedAtUtc is { } changed ? ShopClock.ToLocalDate(changed) : job.DateIn,
            Lines = showMoney ? job.Lines.Select(l => new PublicLine(l.Description, l.Type, l.Quantity, l.UnitPrice, l.LineTotal)).ToList() : [],
            Parts = showMoney ? job.PartsTotal : default,
            Labor = showMoney ? job.LaborTotal : default,
            Subtotal = showMoney ? job.Subtotal : default,
            Discount = showMoney ? job.DiscountValue : default,
            DiscountPercent = showMoney ? job.DiscountPercent : default,
            Vat = showMoney ? job.VatValue : default,
            VatPercent = showMoney ? job.VatPercent : default,
            Total = showMoney ? job.Total : default,
            Received = showMoney ? job.PaidTotal : default,
            Balance = job.Balance,
            PaymentState = job.PaymentState,
        };
    }

    private async Task<PublicShareView?> LoadEstimateAsync(ShareLink link, Shop shop, bool hasLogo, CancellationToken ct)
    {
        var shopId = link.ShopId;
        var estimate = await db.Estimates.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == link.TargetId && e.ShopId == shopId, ct);
        if (estimate is null)
        {
            return null;
        }

        var vehicle = await db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == estimate.VehicleId && v.ShopId == shopId, ct);
        if (vehicle is null)
        {
            return null;
        }

        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == vehicle.CustomerId && c.ShopId == shopId, ct);
        if (customer is null)
        {
            return null;
        }

        estimate.Lines = await db.EstimateLines.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.EstimateId == estimate.Id && l.ShopId == shopId).OrderBy(l => l.Id).ToListAsync(ct);

        return new PublicShareView
        {
            Kind = ShareLinkKind.Estimate,
            ShopName = shop.Name,
            ShopPhone = shop.Phone,
            ShopAddress = shop.Address,
            ShopAccent = shop.AccentColor,
            HasLogo = hasLogo,
            CustomerName = customer.Name,
            VehicleTitle = $"{vehicle.Make} {vehicle.Model}".Trim(),
            VehicleYear = vehicle.Year,
            Plate = vehicle.Plate,
            Label = estimate.Label,
            Date = estimate.CreatedOn,
            ValidUntil = estimate.ValidUntil,
            Lines = estimate.Lines.Select(l => new PublicLine(l.Description, l.Type, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            Subtotal = estimate.Subtotal,
            Discount = estimate.DiscountValue,
            DiscountPercent = estimate.DiscountPercent,
            Vat = estimate.VatValue,
            VatPercent = estimate.VatPercent,
            Total = estimate.Total,
        };
    }
}
