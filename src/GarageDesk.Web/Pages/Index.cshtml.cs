using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Text.Json;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages;

/// <summary>One month on the activity chart.</summary>
public sealed record MonthPoint(string Key, decimal Revenue, int Jobs);

[AllowAnonymous]
public class IndexModel(ApplicationDbContext db, ICurrentStaff currentStaff) : PageModel
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
    public decimal OutstandingTotal { get; private set; }

    public int OutstandingCustomers { get; private set; }

    /// <summary>How far the shop is with its first steps (the dashboard's "Getting started" checklist).</summary>
    public SetupProgress Setup { get; private set; } = new(false, false, false, false, false, false);

    public int? RevenueChangePercent =>
        RevenueLastMonth > 0 ? (int)Math.Round((RevenueThisMonth - RevenueLastMonth) / RevenueLastMonth * 100) : null;

    /// <summary>Revenue and the activity chart are reports: owners only.</summary>
    public bool CanSeeReports { get; private set; }

    /// <summary>v1.4, owner only: payments received minus expenses paid this month (cash basis).</summary>
    public decimal ProfitThisMonth { get; private set; }

    public decimal ReceivedThisMonth { get; private set; }

    public decimal ExpensesThisMonth { get; private set; }

    /// <summary>v1.4: the stock card. Everyone who sees the dashboard sees what is running low; the value at cost is the owner's.</summary>
    public StockSummary Stock { get; private set; } = new(0, 0, [], null);

    // ---- ui-polish-v2: "Today at <shop>" and the money cards (all worked out from the same real data) ----

    /// <summary>"morning", "afternoon" or "evening" by the shop's wall clock (Cairo).</summary>
    public string DayPart { get; private set; } = "morning";

    public string GreetingName { get; private set; } = "";

    /// <summary>Overdue reminders, counted over the whole shop (the list below shows only the first few).</summary>
    public int OverdueReminderCount { get; private set; }

    /// <summary>Cars being worked on right now (status In progress).</summary>
    public int InShopCount => InProgressCount;

    /// <summary>Finished, waiting for the customer (status Done). They are also what "Outstanding" adds up.</summary>
    public int ReadyForPickupCount => DoneCount;

    public int? OutstandingOldestDays { get; private set; }

    public decimal ProfitLastMonth { get; private set; }

    public int? ProfitChangePercent =>
        ProfitLastMonth != 0 ? (int)Math.Round((ProfitThisMonth - ProfitLastMonth) / Math.Abs(ProfitLastMonth) * 100) : null;

    /// <summary>The last months of revenue and profit for the small trend lines (owner only; null when there is too little to draw).</summary>
    public Sparkline? RevenueSpark { get; private set; }

    public Sparkline? ProfitSpark { get; private set; }

    public const int SparkMonths = 6;

    /// <summary>Revenue and job cards over the chart's months, for the line above the chart.</summary>
    public decimal YearRevenue => Months.Sum(m => m.Revenue);

    public int YearJobs => Months.Sum(m => m.Jobs);

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Page();
        }

        var staff = await currentStaff.GetAsync();
        if (staff is null || !staff.IsActive)
        {
            return Forbid();
        }

        // This page is anonymous (the landing page), so the dashboard guards itself: a technician's home is "My jobs".
        // A user with no shop yet is sent to CreateShop by RequireShopPageFilter before they get here.
        if (staff.IsTechnician)
        {
            return RedirectToPage("/Work/Index");
        }

        CanSeeReports = staff.IsOwner;
        Today = ShopClock.Today();
        var hour = ShopClock.ToShopTime(DateTime.UtcNow).Hour;
        DayPart = hour < 12 ? "morning" : hour < 18 ? "afternoon" : "evening";
        GreetingName = staff.DisplayName;
        ShopName = await db.Shops.Select(s => s.Name).FirstOrDefaultAsync();

        CustomerCount = await db.Customers.CountAsync();
        VehicleCount = await db.Vehicles.CountAsync();
        JobCount = await db.JobCards.CountAsync();
        BookedCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.Booked);
        InProgressCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.InProgress);
        DoneCount = await db.JobCards.CountAsync(j => j.Status == JobStatus.Done);
        OpenJobCount = BookedCount + InProgressCount + DoneCount;

        Setup = await SetupProgress.LoadAsync(db);

        var owed = await OutstandingBalances.LoadAsync(db);
        OutstandingTotal = owed.Sum(b => b.Owed);
        OutstandingCustomers = owed.Count;
        OutstandingOldestDays = owed.Count > 0 ? Math.Max(0, Today.DayNumber - owed.Min(b => b.Oldest).DayNumber) : null;

        if (CanSeeReports)
        {
            await LoadActivityAsync();

            var monthStart = new DateOnly(Today.Year, Today.Month, 1);
            var (received, spent) = await ProfitReport.CashAsync(db, monthStart, monthStart.AddMonths(1));
            ReceivedThisMonth = received.Total;
            ExpensesThisMonth = spent;
            ProfitThisMonth = received.Total - spent;

            // The small trend lines and last month's profit, from the same definitions (owner only: it is money).
            var profitSeries = await ProfitReport.MonthlyProfitAsync(db, Today, SparkMonths);
            ProfitLastMonth = profitSeries.Count > 1 ? profitSeries[^2] : 0;
            ProfitSpark = Sparkline.From(profitSeries);
            RevenueSpark = Sparkline.From(Months.TakeLast(SparkMonths).Select(m => m.Revenue).ToList());
        }
        await LoadRemindersAsync();
        Stock = await StockSummary.LoadAsync(db, includeCost: CanSeeReports);

        RecentJobs = await db.JobCards.AsNoTracking()
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Take(6)
            .ToListAsync();
        return Page();
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

        OverdueReminderCount = due.Count(r => r.StatusFor(Today, r.Vehicle?.Mileage ?? 0) == ReminderStatus.Overdue);

        DueReminders = due
            .OrderByDescending(r => r.StatusFor(Today, r.Vehicle?.Mileage ?? 0))
            .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.DueMileage ?? int.MaxValue)
            .Take(6)
            .ToList();
    }
}
