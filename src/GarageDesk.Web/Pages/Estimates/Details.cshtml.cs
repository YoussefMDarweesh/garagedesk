using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Pages.Jobs;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Estimates;

public class DetailsModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer, ShareLinkService shares) : PageModel
{
    public Estimate Estimate { get; private set; } = null!;

    /// <summary>The label of the job card this estimate was converted into (for the link).</summary>
    public int? JobCardId => Estimate.JobCardId;

    [BindProperty]
    public JobLineInput NewLine { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>The live share links of this estimate.</summary>
    public ShareLinksPanel Links { get; private set; } = new(0, []);

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>"Send on WhatsApp": creates the estimate's share link (or reuses the live one) and opens wa.me with the message.</summary>
    public async Task<IActionResult> OnPostShareAsync(int id)
    {
        var share = await shares.PrepareWhatsAppAsync(ShareLinkKind.Estimate, id);
        if (share is null)
        {
            return NotFound();
        }

        if (share.WhatsAppUrl is null)
        {
            return Fail(id, "This customer has no phone number WhatsApp can use. Get the link and send it yourself.");
        }

        return Redirect(share.WhatsAppUrl);
    }

    public async Task<IActionResult> OnPostCreateLinkAsync(int id)
    {
        if (await shares.GetOrCreateAsync(ShareLinkKind.Estimate, id) is null)
        {
            return NotFound();
        }

        StatusMessage = localizer["The link is ready. Copy it below."];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRevokeLinkAsync(int id, int linkId)
    {
        if (!await shares.RevokeAsync(linkId, id, ShareLinkKind.Estimate))
        {
            return NotFound();
        }

        StatusMessage = localizer["The link was revoked. It no longer works."];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddLineAsync(int id)
    {
        var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return NotFound();
        }

        if (estimate.IsLocked)
        {
            return Fail(id, "An accepted estimate can no longer be changed.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(id);
            return Page();
        }

        db.EstimateLines.Add(new EstimateLine
        {
            EstimateId = estimate.Id,
            Type = NewLine.Type,
            Description = NewLine.Description.Trim(),
            Quantity = NewLine.Quantity,
            UnitPrice = NewLine.UnitPrice,
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveLineAsync(int id, int lineId)
    {
        var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return NotFound();
        }

        if (estimate.IsLocked)
        {
            return Fail(id, "An accepted estimate can no longer be changed.");
        }

        var line = await db.EstimateLines.FirstOrDefaultAsync(l => l.Id == lineId && l.EstimateId == id);
        if (line is null)
        {
            return NotFound();
        }

        db.EstimateLines.Remove(line);
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    /// <summary>Draft to Sent, Draft or Sent to Declined, Declined back to Draft. Accepted only happens by converting.</summary>
    public async Task<IActionResult> OnPostStatusAsync(int id, EstimateStatus to)
    {
        var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return NotFound();
        }

        var allowed = (estimate.Status, to) switch
        {
            (EstimateStatus.Draft, EstimateStatus.Sent) => true,
            (EstimateStatus.Draft or EstimateStatus.Sent, EstimateStatus.Declined) => true,
            (EstimateStatus.Declined, EstimateStatus.Draft) => true,
            _ => false,
        };
        if (estimate.IsLocked || !allowed)
        {
            return Fail(id, "That status change is not allowed.");
        }

        estimate.Status = to;
        await db.SaveChangesAsync();
        StatusMessage = localizer["Status updated."];
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Creates the job card from the estimate and links both. Doing it twice just goes to the job card that
    /// already exists, so a double click can never create two.
    /// </summary>
    public async Task<IActionResult> OnPostConvertAsync(int id)
    {
        var estimate = await db.Estimates.Include(e => e.Lines).Include(e => e.Vehicle).FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return NotFound();
        }

        if (estimate.JobCardId is int existing)
        {
            return RedirectToPage("/Jobs/Details", new { id = existing });
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var job = new JobCard
        {
            VehicleId = estimate.VehicleId,
            DateIn = ShopClock.Today(),
            MileageAtVisit = estimate.Vehicle?.Mileage ?? 0,
            Status = JobStatus.Booked,
            Notes = estimate.Notes,
            DiscountAmount = estimate.DiscountAmount,
            DiscountPercent = estimate.DiscountPercent,
            VatPercent = estimate.VatPercent,
            EstimateId = estimate.Id,
        };
        job.Lines.AddRange(estimate.Lines.OrderBy(l => l.Id).Select(l => new JobLine
        {
            Type = l.Type,
            Description = l.Description,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
        }));
        db.JobCards.Add(job);
        await db.SaveChangesAsync();

        estimate.Status = EstimateStatus.Accepted;
        estimate.JobCardId = job.Id;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        StatusMessage = localizer["Estimate accepted. This is the new job card."];
        return RedirectToPage("/Jobs/Details", new { id = job.Id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        // Another shop's estimate id finds nothing (query filter), so it is a plain 404.
        var estimate = await db.Estimates.AsNoTracking()
            .Include(e => e.Vehicle).ThenInclude(v => v!.Customer)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (estimate is null)
        {
            return false;
        }

        estimate.Lines = estimate.Lines.OrderBy(l => l.Id).ToList();
        Estimate = estimate;
        Links = new ShareLinksPanel(estimate.Id, await shares.ListLiveAsync(estimate.Id, ShareLinkKind.Estimate));
        return true;
    }

    private IActionResult Fail(int id, string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage(new { id });
    }
}
