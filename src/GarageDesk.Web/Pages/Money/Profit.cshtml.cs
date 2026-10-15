using GarageDesk.Web.Data;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Money;

/// <summary>Profit for a month, cash basis, plus profit per job card. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class ProfitModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>The month as "yyyy-MM". Anything else means the current month.</summary>
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Month { get; set; }

    public MonthProfit Report { get; private set; } = null!;

    public DateOnly MonthStart => Report.Month;

    public string PreviousKey => MonthStart.AddMonths(-1).ToString("yyyy-MM");

    public string NextKey => MonthStart.AddMonths(1).ToString("yyyy-MM");

    public string CategoryName(string name) => ExpenseCategories.Display(localizer, name);

    public async Task OnGetAsync()
    {
        Report = await ProfitReport.MonthAsync(db, ProfitReport.ParseMonth(Month, ShopClock.Today()));
    }
}
