using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

public class DetailsModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public JobCard Job { get; private set; } = null!;

    [BindProperty]
    public JobLineInput NewLine { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Set right after a job is marked Done: the page then offers to create the next oil change reminder.</summary>
    [TempData]
    public bool OfferReminder { get; set; }

    public JobStatus? NextStatus => JobStatusFlow.Next(Job.Status);

    public JobStatus? PreviousStatus => JobStatusFlow.Previous(Job.Status);

    public async Task<IActionResult> OnGetAsync(int id) =>
        await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAddLineAsync(int id)
    {
        var job = await db.JobCards.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

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
            UnitPrice = NewLine.UnitPrice,
        });
        await db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveLineAsync(int id, int lineId)
    {
        var job = await db.JobCards.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        if (job.IsLocked)
        {
            return Fail(id, "A paid job card can no longer be changed.");
        }

        var line = await db.JobLines.FirstOrDefaultAsync(l => l.Id == lineId && l.JobCardId == id);
        if (line is null)
        {
            return NotFound();
        }

        db.JobLines.Remove(line);
        await db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, JobStatus to)
    {
        var job = await db.JobCards.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        // The server enforces the flow too; the page just doesn't offer buttons for forbidden moves.
        if (!JobStatusFlow.CanMove(job.Status, to))
        {
            return Fail(id, "That status change is not allowed.");
        }

        job.Status = to;

        // The invoice number is given once, the first time the job card is issued (Done or Paid), and never changes.
        if (job.InvoiceNumber is null && to is JobStatus.Done or JobStatus.Paid)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            job.InvoiceNumber = await DocumentNumbers.NextAsync(db, job.ShopId, DocumentNumbers.Invoice);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        else
        {
            await db.SaveChangesAsync();
        }

        StatusMessage = localizer["Status updated."];
        OfferReminder = to == JobStatus.Done;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        // Another shop's job id finds nothing (query filter), so it is a plain 404.
        var job = await db.JobCards
            .AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job is null)
        {
            return false;
        }

        job.Lines = job.Lines.OrderBy(l => l.Id).ToList();
        Job = job;
        return true;
    }

    private IActionResult Fail(int id, string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage(new { id });
    }
}
