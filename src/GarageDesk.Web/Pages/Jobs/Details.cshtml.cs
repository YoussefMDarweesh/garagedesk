using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

public class DetailsModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer, ShareLinkService shares) : PageModel
{
    public JobCard Job { get; private set; } = null!;

    [BindProperty]
    public JobLineInput NewLine { get; set; } = new();

    // Bound only inside the payment handlers (TryUpdateModelAsync), so adding a line never validates the payment form.
    public PaymentInput NewPayment { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Set right after a job is marked Done: the page then offers to create the next oil change reminder.</summary>
    [TempData]
    public bool OfferReminder { get; set; }

    /// <summary>Set right after a job is marked Done: the page then shows a toast offering to tell the customer on WhatsApp.</summary>
    [TempData]
    public bool OfferShare { get; set; }

    /// <summary>The live share links of this job card (status and invoice).</summary>
    public ShareLinksPanel Links { get; private set; } = new(0, []);

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
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).FirstOrDefaultAsync(j => j.Id == id);
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

        // The total must never drop below what the customer has already paid.
        if (job.PaidTotal > JobTotals.Total(job.Subtotal - line.LineTotal, job.DiscountAmount, job.DiscountPercent, job.VatPercent))
        {
            return Fail(id, "Payments already received are more than the new total. Void the latest payment first.");
        }

        db.JobLines.Remove(line);
        await db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddPaymentAsync(int id)
    {
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        if (job.IsLocked)
        {
            return Fail(id, "A paid job card can no longer be changed.");
        }
        // Other bound forms on this page (the line editor) are empty here, so look only at the payment fields.
        await TryUpdateModelAsync(NewPayment, nameof(NewPayment));
        if (ModelState.Where(e => e.Key.StartsWith(nameof(NewPayment))).Any(e => e.Value?.Errors.Count > 0))
        {
            return Fail(id, "The amount must be greater than zero.");
        }

        var amount = Math.Round(NewPayment.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            return Fail(id, "The amount must be greater than zero.");
        }

        if (amount > job.Balance)
        {
            return Fail(id, "The payment is more than the balance due.");
        }

        var balanceBefore = job.Balance;
        db.Payments.Add(new Payment
        {
            JobCardId = job.Id,
            Amount = amount,
            Method = NewPayment.Method,
            PaidOn = ShopClock.Today(),
            Note = string.IsNullOrWhiteSpace(NewPayment.Note) ? null : NewPayment.Note.Trim(),
        });

        // Once a finished job is fully paid it becomes Paid, and Paid is final.
        if (job.Status == JobStatus.Done && amount == balanceBefore)
        {
            job.Status = JobStatus.Paid;
            job.StatusChangedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        StatusMessage = localizer["Payment recorded."];
        return RedirectToPage(new { id });
    }

    /// <summary>Only the latest payment of a job card that is not Paid can be voided (a typing mistake); Paid is final.</summary>
    public async Task<IActionResult> OnPostVoidPaymentAsync(int id, int paymentId)
    {
        var job = await db.JobCards.Include(j => j.Payments).FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        if (job.IsLocked)
        {
            return Fail(id, "A paid job card can no longer be changed.");
        }

        var latest = job.Payments.OrderByDescending(p => p.Id).FirstOrDefault();
        if (latest is null || latest.Id != paymentId)
        {
            return Fail(id, "Only the latest payment can be voided.");
        }

        db.Payments.Remove(latest);
        await db.SaveChangesAsync();
        StatusMessage = localizer["Payment voided."];
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// "Send on WhatsApp": creates the share link (or reuses the live one) and opens wa.me with the message.
    /// Nothing is sent by the server; the owner presses send in WhatsApp.
    /// </summary>
    public async Task<IActionResult> OnPostShareAsync(int id, ShareLinkKind kind)
    {
        if (kind is not (ShareLinkKind.Status or ShareLinkKind.Invoice))
        {
            return BadRequest();
        }

        var share = await shares.PrepareWhatsAppAsync(kind, id);
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

    /// <summary>Creates the link (or finds the live one) without sending anything, so it can be copied below.</summary>
    public async Task<IActionResult> OnPostCreateLinkAsync(int id, ShareLinkKind kind)
    {
        if (kind is not (ShareLinkKind.Status or ShareLinkKind.Invoice))
        {
            return BadRequest();
        }

        if (await shares.GetOrCreateAsync(kind, id) is null)
        {
            return NotFound();
        }

        StatusMessage = localizer["The link is ready. Copy it below."];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRevokeLinkAsync(int id, int linkId)
    {
        if (!await shares.RevokeAsync(linkId, id, ShareLinkKind.Status, ShareLinkKind.Invoice))
        {
            return NotFound();
        }

        StatusMessage = localizer["The link was revoked. It no longer works."];
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, JobStatus to)
    {
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return NotFound();
        }

        // The server enforces the flow too; the page just doesn't offer buttons for forbidden moves.
        if (!JobStatusFlow.CanMove(job.Status, to))
        {
            return Fail(id, "That status change is not allowed.");
        }

        // VAT is fixed when the job card is issued, so later changes of the shop setting never alter an invoice.
        if (job.InvoiceNumber is null && to is JobStatus.Done or JobStatus.Paid)
        {
            job.VatPercent = await db.Shops.Select(s => s.VatPercent).FirstOrDefaultAsync();
        }

        // Marking a job paid by hand records the rest of the balance as a cash payment, so balance and status agree.
        if (to == JobStatus.Paid && job.Balance > 0)
        {
            db.Payments.Add(new Payment { JobCardId = job.Id, Amount = job.Balance, Method = PaymentMethod.Cash, PaidOn = ShopClock.Today(), Note = "Marked as paid" });
        }

        // A job that is done and already fully paid (deposits) is simply paid.
        var requested = to;
        job.Status = to == JobStatus.Done && job.Total > 0 && job.Balance <= 0 ? JobStatus.Paid : to;
        job.StatusChangedAtUtc = DateTime.UtcNow;
        to = job.Status;

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
        OfferShare = requested == JobStatus.Done;
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        // Another shop's job id finds nothing (query filter), so it is a plain 404.
        var job = await db.JobCards
            .AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .Include(j => j.Payments)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job is null)
        {
            return false;
        }

        job.Lines = job.Lines.OrderBy(l => l.Id).ToList();
        job.Payments = job.Payments.OrderBy(p => p.Id).ToList();
        Job = job;
        Links = new ShareLinksPanel(job.Id, await shares.ListLiveAsync(job.Id, ShareLinkKind.Status, ShareLinkKind.Invoice));
        return true;
    }

    private IActionResult Fail(int id, string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage(new { id });
    }
}
