using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Reminders;

[Authorize(Policy = Policies.Staff)]
public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 200;

    /// <summary>"open" (default), "done" or "all".</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    public string Filter => Show is "done" or "all" ? Show : "open";

    public DateOnly Today { get; private set; }

    public string? ShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    public List<Reminder> Reminders { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Today = ShopClock.Today();

        var query = db.Reminders.AsNoTracking()
            .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
            .AsQueryable();

        if (Filter == "open")
        {
            query = query.Where(r => !r.IsDone);
        }
        else if (Filter == "done")
        {
            query = query.Where(r => r.IsDone);
        }

        var found = await query.OrderByDescending(r => r.Id).Take(PageSize).ToListAsync();

        // Most urgent first: overdue, then due soon, then upcoming; earliest date first within each.
        Reminders = found
            .OrderByDescending(r => r.IsDone ? ReminderStatus.Upcoming : r.StatusFor(Today, r.Vehicle?.Mileage ?? 0))
            .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.DueMileage ?? int.MaxValue)
            .ToList();
    }

    public Task<IActionResult> OnPostDoneAsync(int id, string? returnUrl) => SetDoneAsync(id, true, returnUrl);

    public Task<IActionResult> OnPostReopenAsync(int id, string? returnUrl) => SetDoneAsync(id, false, returnUrl);

    private async Task<IActionResult> SetDoneAsync(int id, bool done, string? returnUrl)
    {
        // Another shop's reminder id finds nothing (query filter), so it is a plain 404.
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null)
        {
            return NotFound();
        }

        reminder.IsDone = done;
        await db.SaveChangesAsync();

        // Back to wherever the button was (dashboard, vehicle page, list), but only to a page on this site.
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToPage();
    }
}
