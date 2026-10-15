using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>The part form (add and edit). OpeningQuantity is only used when adding.</summary>
public class PartInput
{
    [Required, StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [StringLength(40)]
    [Display(Name = "SKU (optional)")]
    public string? Sku { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "Unit")]
    public string Unit { get; set; } = "pcs";

    [Range(0, 10_000_000)]
    [Display(Name = "Cost price (EGP)")]
    public decimal CostPrice { get; set; }

    [Range(0, 10_000_000)]
    [Display(Name = "Sell price (EGP)")]
    public decimal SellPrice { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Quantity on hand")]
    public decimal OpeningQuantity { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Low-stock alert at")]
    public decimal? LowStockThreshold { get; set; }

    [StringLength(100)]
    [Display(Name = "Supplier (optional)")]
    public string? Supplier { get; set; }
}

public static class PartRules
{
    /// <summary>
    /// A name or a SKU already used by another part of this shop (archived ones count too). Returns the field name
    /// that clashes, or null. Names are compared ignoring case.
    /// </summary>
    public static async Task<string?> ClashAsync(ApplicationDbContext db, PartInput input, int? exceptId)
    {
        var name = input.Name.Trim().ToLowerInvariant();
        var sku = string.IsNullOrWhiteSpace(input.Sku) ? null : input.Sku.Trim().ToLowerInvariant();

        var others = await db.Parts.AsNoTracking()
            .Where(p => exceptId == null || p.Id != exceptId)
            .Select(p => new { p.Name, p.Sku })
            .ToListAsync();

        if (others.Any(p => p.Name.ToLowerInvariant() == name))
        {
            return nameof(PartInput.Name);
        }

        if (sku is not null && others.Any(p => p.Sku != null && p.Sku.ToLowerInvariant() == sku))
        {
            return nameof(PartInput.Sku);
        }

        return null;
    }
}
