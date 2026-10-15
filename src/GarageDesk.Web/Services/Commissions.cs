using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>One person's commission for one month.</summary>
public sealed record CommissionLine(string UserId, string Name, bool IsActive, decimal Percent, int Jobs, decimal LaborTotal, decimal Due);

/// <summary>
/// The commission rule, computed from stored data (never stored itself):
/// for each job card that is Paid and assigned to the person, whose visit date falls in the month,
/// take its labor lines (quantity x unit price, rounded per line to the piastre; before discount and VAT),
/// add them up, and multiply by the person's commission percent, rounded to the piastre.
/// The percent used is the person's current one.
/// </summary>
public static class Commissions
{
    public static decimal Due(decimal laborTotal, decimal percent) =>
        Math.Round(laborTotal * percent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>People who had at least one paid, assigned job in the month, plus anyone with a percent above zero.</summary>
    public static async Task<List<CommissionLine>> MonthAsync(ApplicationDbContext db, int shopId, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);

        // Only what is needed is read. The query filter limits job cards to this shop; the member lookup states it.
        var jobs = await db.JobCards.AsNoTracking()
            .Where(j => j.Status == JobStatus.Paid && j.AssignedToUserId != null && j.DateIn >= first && j.DateIn < next)
            .Select(j => new
            {
                j.AssignedToUserId,
                Labor = j.Lines.Where(l => l.Type == JobLineType.Labor).Select(l => new { l.Quantity, l.UnitPrice }).ToList(),
            })
            .ToListAsync();

        var perPerson = jobs
            .GroupBy(j => j.AssignedToUserId!)
            .ToDictionary(
                g => g.Key,
                g => (Jobs: g.Count(),
                      Labor: g.Sum(j => j.Labor.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)))));

        var members = await db.MembersOf(shopId).AsNoTracking()
            .Select(u => new { u.Id, u.FullName, u.Email, u.IsActive, u.CommissionPercent })
            .ToListAsync();

        return members
            .Where(m => perPerson.ContainsKey(m.Id) || (m.IsActive && m.CommissionPercent > 0))
            .Select(m =>
            {
                perPerson.TryGetValue(m.Id, out var totals);
                var name = !string.IsNullOrWhiteSpace(m.FullName) ? m.FullName : (m.Email ?? "").Split('@')[0];
                return new CommissionLine(m.Id, name, m.IsActive, m.CommissionPercent, totals.Jobs, totals.Labor, Due(totals.Labor, m.CommissionPercent));
            })
            .OrderByDescending(l => l.Due).ThenBy(l => l.Name)
            .ToList();
    }
}
