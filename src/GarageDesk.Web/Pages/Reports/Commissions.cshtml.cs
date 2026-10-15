using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Reports;

/// <summary>Commissions due per technician for a month. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class CommissionsModel(ApplicationDbContext db, ICurrentShop currentShop) : PageModel
{
    /// <summary>The month as "yyyy-MM". Anything else means the current month.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }

    public DateOnly MonthStart { get; private set; }

    public string PreviousKey => MonthStart.AddMonths(-1).ToString("yyyy-MM");

    public string NextKey => MonthStart.AddMonths(1).ToString("yyyy-MM");

    public List<CommissionLine> Lines { get; private set; } = [];

    public decimal TotalDue => Lines.Sum(l => l.Due);

    public decimal TotalLabor => Lines.Sum(l => l.LaborTotal);

    /// <summary>Paid jobs of the month that nobody was assigned to: they earn nobody a commission.</summary>
    public int UnassignedPaidJobs { get; private set; }

    public async Task OnGetAsync()
    {
        var today = ShopClock.Today();
        MonthStart = DateOnly.TryParseExact((Month ?? "") + "-01", "yyyy-MM-dd", out var parsed) && parsed.Year >= 2000 && parsed <= today.AddYears(1)
            ? new DateOnly(parsed.Year, parsed.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);

        Lines = await Commissions.MonthAsync(db, currentShop.ShopId!.Value, MonthStart.Year, MonthStart.Month);

        var next = MonthStart.AddMonths(1);
        UnassignedPaidJobs = await db.JobCards.CountAsync(j =>
            j.Status == JobStatus.Paid && j.AssignedToUserId == null && j.DateIn >= MonthStart && j.DateIn < next);
    }
}
