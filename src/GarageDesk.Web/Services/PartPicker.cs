using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>A catalog part as the "add a line" picker shows it to the front desk: with the sell price (never the cost).</summary>
public sealed record PartOption(int Id, string Name, string Unit, decimal Quantity, decimal SellPrice);

/// <summary>A catalog part as a technician sees it: name, unit and how many are on the shelf. There is no price field at all.</summary>
public sealed record WorkPartOption(int Id, string Name, string Unit, decimal Quantity);

public static class PartPicker
{
    /// <summary>The active parts of the current shop, by name. The projection never reads the cost price.</summary>
    public static async Task<List<PartOption>> LoadAsync(ApplicationDbContext db) =>
        (await db.Parts.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Unit, p.QuantityOnHand, p.SellPrice })
            .Take(1000)
            .ToListAsync())
        .Select(p => new PartOption(p.Id, p.Name, p.Unit, p.QuantityOnHand, p.SellPrice))
        .ToList();

    /// <summary>The same list for technicians: the sell price is not even read from the database.</summary>
    public static async Task<List<WorkPartOption>> LoadForTechnicianAsync(ApplicationDbContext db) =>
        (await db.Parts.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Unit, p.QuantityOnHand })
            .Take(1000)
            .ToListAsync())
        .Select(p => new WorkPartOption(p.Id, p.Name, p.Unit, p.QuantityOnHand))
        .ToList();
}

/// <summary>What the dashboard shows about stock. The cost (and so the value) is only read when the caller is the owner.</summary>
public sealed record StockSummary(int PartCount, int LowCount, IReadOnlyList<string> LowNames, decimal? StockValue)
{
    public static async Task<StockSummary> LoadAsync(ApplicationDbContext db, bool includeCost)
    {
        var rows = includeCost
            ? (await db.Parts.AsNoTracking().Where(p => p.IsActive)
                .Select(p => new { p.Name, p.QuantityOnHand, p.LowStockThreshold, Cost = (decimal?)p.CostPrice }).ToListAsync())
                .Select(p => (p.Name, Qty: p.QuantityOnHand, Threshold: p.LowStockThreshold, Cost: p.Cost)).ToList()
            : (await db.Parts.AsNoTracking().Where(p => p.IsActive)
                .Select(p => new { p.Name, p.QuantityOnHand, p.LowStockThreshold }).ToListAsync())
                .Select(p => (p.Name, Qty: p.QuantityOnHand, Threshold: p.LowStockThreshold, Cost: (decimal?)null)).ToList();

        var low = rows.Where(r => r.Threshold is { } t && r.Qty <= t).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        decimal? value = includeCost ? rows.Sum(r => JobTotals.Round(Math.Max(r.Qty, 0) * (r.Cost ?? 0))) : null;
        return new StockSummary(rows.Count, low.Count, low.Take(3).Select(r => r.Name).ToList(), value);
    }
}

/// <summary>
/// Per-job profit, worked out from stored data (never stored). Revenue is what the job card earned before VAT
/// (subtotal minus the discount); part cost is the cost each catalog part line had when it was added. Labor has
/// no cost here. Lines typed as free text have no known cost, so they count as zero cost.
/// </summary>
public static class JobProfit
{
    /// <summary>Quantity times the recorded unit cost of every part line that has one, rounded per line to the piastre.</summary>
    public static decimal PartCost(IEnumerable<JobLine> lines) =>
        lines.Where(l => l.Type == JobLineType.Part && l.UnitCost is not null)
             .Sum(l => JobTotals.Round(l.Quantity * l.UnitCost!.Value));

    /// <summary>How many part lines have no recorded cost (free text), so the profit may be overstated.</summary>
    public static int PartLinesWithoutCost(IEnumerable<JobLine> lines) =>
        lines.Count(l => l.Type == JobLineType.Part && l.UnitCost is null);

    /// <summary>Subtotal minus the discount: what the job earned before VAT.</summary>
    public static decimal Revenue(JobCard job) => JobTotals.Round(job.Subtotal - job.DiscountValue);

    public static decimal Of(JobCard job) => Revenue(job) - PartCost(job.Lines);
}
