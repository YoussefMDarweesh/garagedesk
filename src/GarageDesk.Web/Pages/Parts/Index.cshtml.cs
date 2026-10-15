using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>One catalog row. CostPrice is null for everyone but the owner (it is not even read from the database).</summary>
public sealed record PartRow(int Id, string Name, string? Sku, string Unit, decimal SellPrice, decimal Quantity, decimal? Threshold, bool IsActive, decimal? CostPrice)
{
    public bool IsLow => IsActive && Threshold is { } t && Quantity <= t;

    /// <summary>Quantity on the shelf times cost (never below zero), to the piastre. Owner only.</summary>
    public decimal? StockValue => CostPrice is { } c ? JobTotals.Round(Math.Max(Quantity, 0) * c) : null;
}

/// <summary>
/// The parts catalog and the low-stock list. Owners and cashiers can look (cashiers see sell prices and quantities,
/// never the cost price or the stock value); only owners add, edit and receive stock.
/// </summary>
[Authorize(Policy = Policies.Staff)]
public class IndexModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public const int PageSize = 30;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"" = active parts, "low" = running low, "archived".</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<PartRow> Parts { get; private set; } = [];

    public int Total { get; private set; }

    public int PageCount { get; private set; } = 1;

    /// <summary>How many active parts are running low (always counted over the whole catalog, not the filtered list).</summary>
    public int LowCount { get; private set; }

    public bool IsOwner { get; private set; }

    /// <summary>What the stock on the shelf cost, over all active parts. Owner only.</summary>
    public decimal? StockValue { get; private set; }

    public bool AllowNegativeStock { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public PagerModel Pager => new(P, PageCount, Total, PageSize, "/Parts/Index",
        new Dictionary<string, string?> { ["q"] = Q, ["show"] = Show });

    public async Task OnGetAsync()
    {
        IsOwner = (await currentStaff.GetAsync())?.IsOwner == true;

        // The cost price is only read for the owner. For everyone else it is not in the query at all.
        var rows = IsOwner
            ? (await db.Parts.AsNoTracking()
                .Select(p => new { p.Id, p.Name, p.Sku, p.Unit, p.SellPrice, p.QuantityOnHand, p.LowStockThreshold, p.IsActive, p.CostPrice })
                .ToListAsync())
                .Select(p => new PartRow(p.Id, p.Name, p.Sku, p.Unit, p.SellPrice, p.QuantityOnHand, p.LowStockThreshold, p.IsActive, p.CostPrice))
                .ToList()
            : (await db.Parts.AsNoTracking()
                .Select(p => new { p.Id, p.Name, p.Sku, p.Unit, p.SellPrice, p.QuantityOnHand, p.LowStockThreshold, p.IsActive })
                .ToListAsync())
                .Select(p => new PartRow(p.Id, p.Name, p.Sku, p.Unit, p.SellPrice, p.QuantityOnHand, p.LowStockThreshold, p.IsActive, null))
                .ToList();

        // Money is compared in memory, like the rest of the app (SQLite and PostgreSQL treat decimals differently).
        LowCount = rows.Count(r => r.IsLow);
        if (IsOwner)
        {
            StockValue = rows.Where(r => r.IsActive).Sum(r => r.StockValue ?? 0);
            AllowNegativeStock = await StockLedger.AllowsNegativeAsync(db);
        }

        IEnumerable<PartRow> view = Show switch
        {
            "archived" => rows.Where(r => !r.IsActive),
            "low" => rows.Where(r => r.IsLow),
            _ => rows.Where(r => r.IsActive),
        };

        var text = Q?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            view = view.Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || (r.Sku?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = view.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id).ToList();
        Total = list.Count;
        PageCount = PagerModel.CountPages(Total, PageSize);
        P = Math.Clamp(P, 1, PageCount);
        Parts = list.Skip((P - 1) * PageSize).Take(PageSize).ToList();
    }

    /// <summary>The owner's choice: may a part go below zero? (Off by default: the picker refuses what is not on the shelf.)</summary>
    public async Task<IActionResult> OnPostAllowNegativeAsync(bool allow)
    {
        // The page policy is Staff (cashiers may look), so the owner check is made here, on the server, for this handler.
        if ((await currentStaff.GetAsync())?.IsOwner != true)
        {
            return Forbid();
        }

        var shop = await db.Shops.FirstAsync();
        shop.AllowNegativeStock = allow;
        await db.SaveChangesAsync();
        StatusMessage = allow
            ? localizer["Negative stock is now allowed."]
            : localizer["Negative stock is no longer allowed."];
        return RedirectToPage();
    }
}
