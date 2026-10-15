using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Estimates;

/// <summary>The printable A4 estimate. Same paper as the invoice, without the payment block.</summary>
[Authorize(Policy = Policies.Staff)]
public class PrintModel(ApplicationDbContext db) : PageModel
{
    public Estimate Estimate { get; private set; } = null!;

    public Shop Shop { get; private set; } = null!;

    public string? LogoVersion => User.FindFirst(CurrentShop.LogoClaimType)?.Value;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's estimate id finds nothing (query filter): a plain 404.
        var estimate = await db.Estimates.AsNoTracking()
            .Include(e => e.Vehicle).ThenInclude(v => v!.Customer)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return NotFound();
        }

        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync();
        if (shop is null)
        {
            return NotFound();
        }

        estimate.Lines = estimate.Lines.OrderBy(l => l.Id).ToList();
        Estimate = estimate;
        Shop = shop;
        return Page();
    }
}
