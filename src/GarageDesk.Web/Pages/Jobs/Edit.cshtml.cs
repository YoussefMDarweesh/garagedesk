using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

public class EditModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public JobInput Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public JobCard Job { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var job = await LoadAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        if (job.IsLocked)
        {
            return Locked(id);
        }

        Input.DateIn = job.DateIn;
        Input.MileageAtVisit = job.MileageAtVisit;
        Input.Notes = job.Notes;
        Input.DiscountAmount = job.DiscountAmount;
        Input.DiscountPercent = job.DiscountPercent;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var job = await db.JobCards.Include(j => j.Vehicle).ThenInclude(v => v!.Customer).FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        Job = job;
        if (job.IsLocked)
        {
            return Locked(id);
        }

        if (Input.DateIn < new DateOnly(2000, 1, 1))
        {
            ModelState.AddModelError("Input.DateIn", localizer["Enter a valid date."]);
        }

        if (Input.DiscountAmount is > 0 && Input.DiscountPercent is > 0)
        {
            ModelState.AddModelError("Input.DiscountPercent", localizer["Use either a discount amount or a percent, not both."]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        job.DateIn = Input.DateIn;
        job.MileageAtVisit = Input.MileageAtVisit;
        job.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        job.DiscountAmount = Input.DiscountAmount is > 0 ? Input.DiscountAmount : null;
        job.DiscountPercent = Input.DiscountPercent is > 0 ? Input.DiscountPercent : null;

        // Correcting a reading upwards updates the vehicle too; a lower one never winds the vehicle back.
        job.Vehicle?.RaiseMileageTo(job.MileageAtVisit);

        await db.SaveChangesAsync();

        return RedirectToPage("Details", new { id });
    }

    private async Task<JobCard?> LoadAsync(int id)
    {
        // Another shop's job id finds nothing (query filter), so it is a plain 404.
        Job = (await db.JobCards.AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .FirstOrDefaultAsync(j => j.Id == id))!;
        return Job;
    }

    private IActionResult Locked(int id)
    {
        ErrorMessage = localizer["A paid job card can no longer be changed."];
        return RedirectToPage("Details", new { id });
    }
}
