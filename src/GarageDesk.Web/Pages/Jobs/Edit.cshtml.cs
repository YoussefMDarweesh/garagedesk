using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

[Authorize(Policy = Policies.Staff)]
public class EditModel(ApplicationDbContext db, ICurrentShop currentShop, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public JobInput Input { get; set; } = new();

    /// <summary>The people this job can be given to (active technicians and owners of this shop).</summary>
    public List<SelectListItem> AssigneeOptions { get; private set; } = [];

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
        Input.AssignedToUserId = job.AssignedToUserId;
        await LoadAssigneesAsync();
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

        // The job may keep its current assignee even if that person has since been deactivated; a new one must be
        // an active technician or owner of THIS shop, whatever user id was posted.
        var assignee = string.IsNullOrWhiteSpace(Input.AssignedToUserId) ? null : Input.AssignedToUserId;
        if (assignee is not null && assignee != job.AssignedToUserId
            && !await db.CanBeAssignedAsync(currentShop.ShopId!.Value, assignee))
        {
            ModelState.AddModelError("Input.AssignedToUserId", localizer["Choose someone from your team."]);
        }

        if (!ModelState.IsValid)
        {
            await LoadAssigneesAsync();
            return Page();
        }

        if (assignee != job.AssignedToUserId)
        {
            var person = assignee is null
                ? null
                : await db.MembersOf(currentShop.ShopId!.Value).AsNoTracking().FirstAsync(u => u.Id == assignee);
            JobAudit.Add(db, await currentStaff.GetAsync(), job, JobAuditKind.Assigned, person?.DisplayLabel ?? "");
        }

        job.AssignedToUserId = assignee;
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

    private async Task LoadAssigneesAsync()
    {
        var people = await db.AssigneesAsync(currentShop.ShopId!.Value);

        // Keep a deactivated current assignee in the list, so saving the form does not silently unassign them.
        if (Input.AssignedToUserId is { Length: > 0 } current && people.All(p => p.Id != current))
        {
            var existing = await db.MembersOf(currentShop.ShopId!.Value).AsNoTracking().FirstOrDefaultAsync(u => u.Id == current);
            if (existing is not null)
            {
                people.Add(existing);
            }
        }

        AssigneeOptions = people.ToOptions(Input.AssignedToUserId);
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
