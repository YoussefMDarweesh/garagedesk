using System.Globalization;
using System.Text.Json;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages;

/// <summary>One month on the activity chart.</summary>
public sealed record MonthPoint(string Key, decimal Revenue, int Jobs);

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int ChartMonths = 12;

    public string? ShopName { get; private set; }

    public DateOnly Today { get; private set; }

    // ---- the numbers on the cards (every query below goes through the shop query filter) ----

    public int CustomerCount { get; private set; }

    public int VehicleCount { get; private set; }

    public int JobCount { get; private set; }

    /// <summary>Job cards that are not paid yet.</summary>
    public int OpenJobCount { get; private set; }

    public int BookedCount { get; private set; }

    public int InProgressCount { get; private set; }

    public int DoneCount { get; private set; }

    /// <summary>The total of the paid job cards whose visit date is in the current month.</summary>
    public decimal RevenueThisMonth { get; private set; }

    public decimal RevenueLastMonth { get; private set; }

    public int PaidThisMonthCount { get; private set; }

    // ---- the chart, the lists ----

    public List<MonthPoint> Months { get; private set; } = [];

    public string ChartJson { get; private set; } = "{\"months\":[]}";

    public bool HasChartData => Months.Any(m => m.Jobs > 0);

    /// <summary>Reminders that are overdue or due in the next 14 days, most urgent first.</summary>
    public List<Reminder> DueReminders { get; private set; } = [];

    /// <summary>The newest job cards, for the "recent service history" timeline.</summary>
    public List<JobCard> RecentJobs { get; private set; } = [];

    public string? ReminderShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    /// <summary>The revenue change against last month, as a whole percentage, or null if there is no basis for one.</summary>
    public int? RevenueChangePercent =>
        RevenueLastMonth > 0 ? (int)Math.Round((RevenueThisMonth - RevenueLastMonth) / RevenueLastMonth * 100) : null;

    public async Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Today = ShopClock.Today();
        ShopName = await db.Shops.Select(s => s.Name).FirstOrDefaultAsync();

        CustomerCount = await db.Customers.CountAsync();
        VehicleCount = await db.Vehicles.CountAsync();
        JobCount = await db.JobCards.CountAsync();
        BookedCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.Booked);
        InProgressCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.InProgress);
        DoneCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.Done);
        OpenJobCount = BookedCount + InProgressCount + DoneCount;

        await LoadActivityAsync();
        await LoadRemindersAsync();

        RecentJobs = await db.JobCards.AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Take(6)
            .ToListAsync();
    }

    private async Task LoadActivityAsync()
    {
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var first = thisMonth.AddMonths(-(ChartMonths - 1));

        // Totals are worked out in memory from the lines (see JobLine.LineTotal), so only what's needed is loaded.
        var jobs = await db.JobCards.AsNoTracking()
            .Where(j => j.DateIn >= first)
            .Select(j => new
            {
                j.DateIn,
                j.Status,
                Lines = j.Lines.Select(l => new { l.Quantity, l.UnitPrice }).ToList(),
            })
            .ToListAsync();

        var months = new List<MonthPoint>();
        for (var m = first; m <= thisMonth; m = m.AddMonths(1))
        {
            var inMonth = jobs.Where(j => j.DateIn.Year == m.Year && j.DateIn.Month == m.Month).ToList();
            var revenue = inMonth
                .Where(j => j.Status == JobStatus.Paid)
                .Sum(j => j.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)));
            months.Add(new MonthPoint($"{m.Year:0000}-{m.Month:00}", revenue, inMonth.Count));
        }

        Months = months;
        RevenueThisMonth = months[^1].Revenue;
        RevenueLastMonth = months.Count > 1 ? months[^2].Revenue : 0;
        PaidThisMonthCount = jobs.Count(j => j.Status == JobStatus.Paid && j.DateIn >= thisMonth);

        ChartJson = JsonSerializer.Serialize(new
        {
            months = months.Select(m => new { key = m.Key, revenue = m.Revenue, jobs = m.Jobs }),
        });
    }

    private async Task LoadRemindersAsync()
    {
        var due = await db.Reminders.AsNoTracking()
            .DueOrOverdue(Today)
            .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
            .Take(200)
            .ToListAsync();

        DueReminders = due
            .OrderByDescending(r => r.StatusFor(Today, r.Vehicle?.Mileage ?? 0))
            .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.DueMileage ?? int.MaxValue)
            .Take(6)
            .ToList();
    }
}
