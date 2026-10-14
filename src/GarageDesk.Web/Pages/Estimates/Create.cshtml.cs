using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Estimates;

public class EstimateInput
{
    [Display(Name = "Vehicle")]
    public int VehicleId { get; set; }

    [Display(Name = "Valid until")]
    public DateOnly? ValidUntil { get; set; }

    [Range(0, 10_000_000)]
    [Display(Name = "Discount (EGP)")]
    public decimal? DiscountAmount { get; set; }

    [Range(0, 100)]
    [Display(Name = "Discount (%)")]
    public decimal? DiscountPercent { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public class CreateModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public const int ValidDays = 14;

    [BindProperty]
    public EstimateInput Input { get; set; } = new();

    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? vehicleId)
    {
        Input.ValidUntil = ShopClock.Today().AddDays(ValidDays);
        if (vehicleId is int id)
        {
            // Another shop's vehicle finds nothing (query filter).
            if (!await db.Vehicles.AnyAsync(v => v.Id == id))
            {
                return NotFound();
            }
            Input.VehicleId = id;
        }
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var vehicleExists = Input.VehicleId > 0 && await db.Vehicles.AnyAsync(v => v.Id == Input.VehicleId);
        if (!vehicleExists)
        {
            ModelState.AddModelError("Input.VehicleId", localizer["Choose the vehicle."]);
        }

        if (Input.DiscountAmount is > 0 && Input.DiscountPercent is > 0)
        {
            ModelState.AddModelError("Input.DiscountPercent", localizer["Use either a discount amount or a percent, not both."]);
        }

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        // Number and row are saved together so a failure leaves no gap in EST-0001, EST-0002, ...
        await using var tx = await db.Database.BeginTransactionAsync();
        var shopId = await db.Shops.Select(s => s.Id).FirstAsync();
        var estimate = new Estimate
        {
            VehicleId = Input.VehicleId,
            CreatedOn = ShopClock.Today(),
            ValidUntil = Input.ValidUntil,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            DiscountAmount = Input.DiscountAmount is > 0 ? Input.DiscountAmount : null,
            DiscountPercent = Input.DiscountPercent is > 0 ? Input.DiscountPercent : null,
            VatPercent = await db.Shops.Select(s => s.VatPercent).FirstOrDefaultAsync(),
            Number = await DocumentNumbers.NextAsync(db, shopId, DocumentNumbers.Estimate),
        };
        db.Estimates.Add(estimate);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return RedirectToPage("Details", new { id = estimate.Id });
    }

    private async Task LoadOptionsAsync()
    {
        var vehicles = await db.Vehicles.AsNoTracking().OrderBy(v => v.Plate)
            .Select(v => new { v.Id, v.Plate, v.Make, v.Model, Owner = v.Customer!.Name })
            .Take(500).ToListAsync();
        VehicleOptions = vehicles.Select(v => new SelectListItem($"{v.Plate} · {v.Make} {v.Model} ({v.Owner})", v.Id.ToString())).ToList();
    }
}
