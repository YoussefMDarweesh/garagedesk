using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// Moving a job card to another status, shared by the owner/cashier job page and the technician's page so the
/// two can never disagree (invoice number, VAT freezing, auto-paid).
/// </summary>
public static class JobWorkflow
{
    /// <summary>
    /// Applies the move (the caller has already checked <see cref="JobStatusFlow.CanMove"/>) and saves.
    /// The job must be loaded with its Lines and Payments. Returns the status the job ended up in.
    /// </summary>
    public static async Task<JobStatus> ChangeStatusAsync(ApplicationDbContext db, JobCard job, JobStatus to)
    {
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
        job.Status = to == JobStatus.Done && job.Total > 0 && job.Balance <= 0 ? JobStatus.Paid : to;
        job.StatusChangedAtUtc = DateTime.UtcNow; // the public status page shows "last updated"
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

        return to;
    }
}
