using System.Globalization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// The only code that changes a part's quantity. Every change is a <see cref="StockMovement"/> added in the same
/// save as the new quantity, so the quantity on hand is always the sum of the movements.
/// Callers add the movement here and then call SaveChanges themselves, together with the change it belongs to
/// (the job line, the audit entry), so either everything is saved or nothing is.
/// </summary>
public static class StockLedger
{
    /// <summary>Messages returned by the stock and job-line code. The pages translate them; a test checks each has an Arabic text.</summary>
    public static class Messages
    {
        public const string Locked = "A paid job card can no longer be changed.";
        public const string UnknownPart = "That part is not in your catalog.";
        public const string NotEnough = "Not enough in stock: {0} left of {1}.";
        public const string Changed = "The stock changed while you were saving. Please try again.";
        public const string PaidMoreThanTotal = "Payments already received are more than the new total. Void the latest payment first.";
        public const string ConvertShort = "Not enough stock of {0} to start this job ({1} left, {2} needed). Receive stock first, or allow negative stock on the Parts page.";
        public const string NegativeNotAllowed = "That would take the stock below zero. Allow negative stock on the Parts page if you really need that.";

        public static readonly string[] All = [Locked, UnknownPart, NotEnough, Changed, PaidMoreThanTotal, ConvertShort, NegativeNotAllowed];
    }

    /// <summary>Does the shop let a part go below zero? Read through the tenant filter, so it is this shop's setting.</summary>
    public static async Task<bool> AllowsNegativeAsync(ApplicationDbContext db) =>
        await db.Shops.Select(s => s.AllowNegativeStock).FirstOrDefaultAsync();

    /// <summary>Changes the quantity and queues the movement. Does not save and does not check the sign: the caller decides.</summary>
    public static StockMovement Apply(
        ApplicationDbContext db, Part part, StockMovementKind kind, decimal change, StaffContext? actor,
        JobCard? job = null, decimal? unitCost = null, string note = "")
    {
        // Quantities are kept to two decimals (that is what the database stores), so the ledger and the part agree exactly.
        change = JobTotals.Round(change);
        part.QuantityOnHand = JobTotals.Round(part.QuantityOnHand + change);
        var movement = new StockMovement
        {
            Part = part,
            Kind = kind,
            QuantityChange = change,
            QuantityAfter = part.QuantityOnHand,
            UnitCost = unitCost,
            JobCard = job,
            Note = note.Length > 200 ? note[..200] : note,
            UserId = actor?.UserId,
            UserName = actor?.DisplayName ?? "",
            AtUtc = DateTime.UtcNow,
        };
        db.StockMovements.Add(movement);
        return movement;
    }

    /// <summary>The audit detail "name|quantity". The name is cleaned of the separators the history page splits on.</summary>
    public static string AuditDetail(string partName, decimal quantity)
    {
        var name = partName.Replace('|', ' ').Replace('>', ' ');
        if (name.Length > 150)
        {
            name = name[..150];
        }

        return name + "|" + quantity.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

/// <summary>What happened when a line was added or removed. <see cref="Error"/> is one of <see cref="StockLedger.Messages"/>.</summary>
public sealed record LineOutcome(bool Ok, string? Error = null, object[]? Args = null, JobLine? Line = null)
{
    public static LineOutcome Success(JobLine? line = null) => new(true, Line: line);

    public static LineOutcome Fail(string error, params object[] args) => new(false, error, args);
}

/// <summary>
/// Adding and removing job card lines, shared by the front desk's page and the technician's page so stock, cost
/// snapshots and the history can never disagree between them.
/// </summary>
public static class JobLines
{
    /// <summary>
    /// Adds a line. With a catalog part: the part must exist in this shop and be active, stock is taken (unless the
    /// shop allows negative stock the shelf must hold enough), the part's current cost is copied onto the line, and
    /// the price is the catalog sell price when none was given. Saves everything in one go.
    /// </summary>
    /// <param name="priceFromCatalog">When true the catalog sell price is always used (the technician cannot set prices).</param>
    public static async Task<LineOutcome> AddAsync(
        ApplicationDbContext db, StaffContext? actor, JobCard job, JobLineType type, string description,
        decimal quantity, decimal unitPrice, int? partId, bool priceFromCatalog = false)
    {
        if (job.IsLocked)
        {
            return LineOutcome.Fail(StockLedger.Messages.Locked);
        }

        quantity = JobTotals.Round(quantity); // two decimals, as stored
        Part? part = null;
        if (partId is int id)
        {
            // The tenant filter means another shop's part id simply finds nothing.
            part = await db.Parts.FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
            if (part is null)
            {
                return LineOutcome.Fail(StockLedger.Messages.UnknownPart);
            }

            if (!await StockLedger.AllowsNegativeAsync(db) && part.QuantityOnHand < quantity)
            {
                return LineOutcome.Fail(StockLedger.Messages.NotEnough, part.QuantityOnHand.ToString("0.##", CultureInfo.InvariantCulture), part.Name);
            }

            type = JobLineType.Part;
            if (priceFromCatalog || unitPrice == 0)
            {
                unitPrice = part.SellPrice;
            }
        }

        var line = new JobLine
        {
            JobCardId = job.Id,
            Type = type,
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            PartId = part?.Id,
            UnitCost = part?.CostPrice,
            AddedByUserId = actor?.UserId,
        };
        db.JobLines.Add(line);

        if (part is not null)
        {
            StockLedger.Apply(db, part, StockMovementKind.JobUse, -quantity, actor, job, note: part.Name);
            JobAudit.Add(db, actor, job, JobAuditKind.PartUsed, StockLedger.AuditDetail(part.Name, quantity));
        }

        return await SaveAsync(db, line);
    }

    /// <summary>
    /// Removes a line and gives its stock back. The job must be loaded with its Lines and Payments. Refused when the
    /// job is paid, or when the new total would fall below what the customer already paid.
    /// </summary>
    public static async Task<LineOutcome> RemoveAsync(ApplicationDbContext db, StaffContext? actor, JobCard job, JobLine line)
    {
        if (job.IsLocked)
        {
            return LineOutcome.Fail(StockLedger.Messages.Locked);
        }

        // The total must never drop below what the customer has already paid.
        if (job.PaidTotal > JobTotals.Total(job.Subtotal - line.LineTotal, job.DiscountAmount, job.DiscountPercent, job.VatPercent))
        {
            return LineOutcome.Fail(StockLedger.Messages.PaidMoreThanTotal);
        }

        if (line.PartId is int partId && line.Type == JobLineType.Part)
        {
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
            if (part is not null)
            {
                StockLedger.Apply(db, part, StockMovementKind.JobReturn, line.Quantity, actor, job, note: part.Name);
                JobAudit.Add(db, actor, job, JobAuditKind.PartReturned, StockLedger.AuditDetail(part.Name, line.Quantity));
            }
        }

        db.JobLines.Remove(line);
        return await SaveAsync(db, null);
    }

    private static async Task<LineOutcome> SaveAsync(ApplicationDbContext db, JobLine? line)
    {
        try
        {
            await db.SaveChangesAsync();
            return LineOutcome.Success(line);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else changed the same part's quantity at the same moment. Nothing was saved.
            return LineOutcome.Fail(StockLedger.Messages.Changed);
        }
    }
}
