using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Work;

/// <summary>A line as a technician sees it: what and how many. There is no price field to leak.</summary>
public sealed record WorkLine(int Id, JobLineType Type, string Description, decimal Quantity, bool CanRemove);

/// <summary>A job card as a technician sees it. Built field by field, so money and phone numbers cannot slip in.</summary>
public sealed record WorkJob(
    int Id, DateOnly DateIn, JobStatus Status, int VehicleId, string Plate, string Make, string Model, int Year,
    string? OwnerName, int MileageAtVisit, string? Notes, List<WorkLine> Lines);

/// <summary>A parts or labor note: description and quantity only. The price is filled in by the front desk.</summary>
public class WorkLineInput
{
    [Display(Name = "Type")]
    public JobLineType Type { get; set; } = JobLineType.Part;

    [Required, StringLength(200)]
    [Display(Name = "Description")]
    public string Description { get; set; } = "";

    [Range(0.01, 100_000)]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1;
}

/// <summary>
/// The technician's job card: change the status (Booked, In progress, Done) and note the parts and labor used.
/// Prices, totals, payments, discounts, invoices and phone numbers do not exist on this page.
/// </summary>
[Authorize(Policy = Policies.Team)]
public class JobModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public WorkJob Job { get; private set; } = null!;

    [BindProperty]
    public WorkLineInput NewLine { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool IsClosed => Job.Status == JobStatus.Paid;

    /// <summary>What the technician is shown: a paid job just looks finished.</summary>
    public JobStatus ShownStatus => Job.Status == JobStatus.Paid ? JobStatus.Done : Job.Status;

    public JobStatus? NextStatus => IsClosed ? null : Allowed(JobStatusFlow.Next(Job.Status));

    public JobStatus? PreviousStatus => IsClosed ? null : Allowed(JobStatusFlow.Previous(Job.Status));

    // Paid is the front desk's step (it records money), so it is never offered here.
    private static JobStatus? Allowed(JobStatus? status) => status == JobStatus.Paid ? null : status;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var result = await GuardAsync(id);
        if (result is not null)
        {
            return result;
        }

        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, JobStatus to)
    {
        var denied = await GuardAsync(id);
        if (denied is not null)
        {
            return denied;
        }

        // Tracked, with the lines and payments the shared workflow needs (they are never shown on this page).
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).FirstAsync(j => j.Id == id);
        if (to == JobStatus.Paid || !JobStatusFlow.CanMove(job.Status, to))
        {
            return Fail(id, "That status change is not allowed.");
        }

        await JobWorkflow.ChangeStatusAsync(db, job, to, await currentStaff.GetAsync());
        StatusMessage = localizer["Status updated."];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddLineAsync(int id)
    {
        var denied = await GuardAsync(id);
        if (denied is not null)
        {
            return denied;
        }

        var job = await db.JobCards.FirstAsync(j => j.Id == id);
        if (job.IsLocked)
        {
            return Fail(id, "A paid job card can no longer be changed.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(id);
            return Page();
        }

        db.JobLines.Add(new JobLine
        {
            JobCardId = job.Id,
            Type = NewLine.Type,
            Description = NewLine.Description.Trim(),
            Quantity = NewLine.Quantity,
            UnitPrice = 0, // priced later by the owner or cashier
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveLineAsync(int id, int lineId)
    {
        var denied = await GuardAsync(id);
        if (denied is not null)
        {
            return denied;
        }

        var job = await db.JobCards.FirstAsync(j => j.Id == id);
        if (job.IsLocked)
        {
            return Fail(id, "A paid job card can no longer be changed.");
        }

        // Only a note that has not been priced yet can be taken back; a priced line belongs to the front desk.
        var line = await db.JobLines.FirstOrDefaultAsync(l => l.Id == lineId && l.JobCardId == id && l.UnitPrice == 0);
        if (line is null)
        {
            return NotFound();
        }

        db.JobLines.Remove(line);
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// 404 if the job is not in this shop (the query filter hides other shops' jobs); 403 if it is a colleague's job
    /// and the caller is a technician. Null means: go ahead.
    /// </summary>
    private async Task<IActionResult?> GuardAsync(int id)
    {
        var job = await db.JobCards.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new { j.AssignedToUserId })
            .FirstOrDefaultAsync();
        if (job is null)
        {
            return NotFound();
        }

        var staff = (await currentStaff.GetAsync())!;
        if (staff.IsTechnician && job.AssignedToUserId != staff.UserId)
        {
            return Forbid();
        }

        return null;
    }

    private async Task LoadAsync(int id)
    {
        // Projected field by field: UnitPrice is only used to decide whether a line may be taken back, never sent on.
        var job = await db.JobCards.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new
            {
                j.Id,
                j.DateIn,
                j.Status,
                j.VehicleId,
                j.Vehicle!.Plate,
                j.Vehicle.Make,
                j.Vehicle.Model,
                j.Vehicle.Year,
                Owner = j.Vehicle.Customer!.Name,
                j.MileageAtVisit,
                j.Notes,
                Lines = j.Lines.OrderBy(l => l.Id).Select(l => new { l.Id, l.Type, l.Description, l.Quantity, Unpriced = l.UnitPrice == 0 }).ToList(),
            })
            .FirstAsync();

        Job = new WorkJob(
            job.Id, job.DateIn, job.Status, job.VehicleId, job.Plate, job.Make, job.Model, job.Year, job.Owner,
            job.MileageAtVisit, job.Notes,
            job.Lines.Select(l => new WorkLine(l.Id, l.Type, l.Description, l.Quantity, l.Unpriced)).ToList());
    }

    private IActionResult Fail(int id, string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage(new { id });
    }
}
