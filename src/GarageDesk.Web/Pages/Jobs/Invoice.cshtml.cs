using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

/// <summary>
/// The printable invoice of one job card. It shows exactly what the job card page shows: the totals come from
/// JobCard.Total / PartsTotal / LaborTotal (the same code), never from a second calculation.
/// </summary>
public class InvoiceModel(ApplicationDbContext db, ShareLinkService shares, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public JobCard Job { get; private set; } = null!;

    public Shop Shop { get; private set; } = null!;

    /// <summary>Set when the shop has a logo; the image is fetched from the (signed-in only) logo endpoint.</summary>
    public string? LogoVersion => User.FindFirst(CurrentShop.LogoClaimType)?.Value;

    public bool IsPaid => Job.Status == JobStatus.Paid;

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>"Send on WhatsApp" from the invoice: creates (or reuses) the invoice link and opens WhatsApp with it.</summary>
    public async Task<IActionResult> OnPostShareAsync(int id)
    {
        var share = await shares.PrepareWhatsAppAsync(ShareLinkKind.Invoice, id);
        if (share is null)
        {
            return NotFound();
        }

        if (share.WhatsAppUrl is null)
        {
            ErrorMessage = localizer["This customer has no phone number WhatsApp can use. Get the link and send it yourself."];
            return RedirectToPage(new { id });
        }

        return Redirect(share.WhatsAppUrl);
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's job id finds nothing (query filter), so it is a plain 404, never someone else's invoice.
        var job = await db.JobCards
            .AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .Include(j => j.Payments)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job is null)
        {
            return NotFound();
        }

        // The query filter limits Shops to the signed-in user's own shop.
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync();
        if (shop is null)
        {
            return NotFound();
        }

        job.Lines = job.Lines.OrderBy(l => l.Id).ToList();
        Job = job;
        Shop = shop;
        return Page();
    }
}
