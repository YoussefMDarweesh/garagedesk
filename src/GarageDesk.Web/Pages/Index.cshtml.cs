using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public string? ShopName { get; private set; }

    public int CustomerCount { get; private set; }

    public int VehicleCount { get; private set; }

    public int JobCount { get; private set; }

    public DateOnly Today { get; private set; }

    /// <summary>Reminders that are overdue or due in the next 14 days, most urgent first.</summary>
    public List<Reminder> DueReminders { get; private set; } = [];

    public string? ReminderShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    /// <summary>Job cards that are not paid yet.</summary>
    public int OpenJobCount { get; private set; }

    public async Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // The query filter limits all of these to the signed-in user's own shop.
            ShopName = await db.Shops.Select(s => s.Name).FirstOrDefaultAsync();
            CustomerCount = await db.Customers.CountAsync();
            VehicleCount = await db.Vehicles.CountAsync();
            JobCount = await db.JobCards.CountAsync();
            OpenJobCount = await db.JobCards.CountAsync(j => j.Status != JobStatus.Paid);

            Today = ShopClock.Today();
            var due = await db.Reminders.AsNoTracking()
                .DueOrOverdue(Today)
                .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
                .Take(200)
                .ToListAsync();

            DueReminders = due
                .OrderByDescending(r => r.StatusFor(Today, r.Vehicle?.Mileage ?? 0))
                .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
                .ThenBy(r => r.DueMileage ?? int.MaxValue)
                .Take(20)
                .ToList();
        }
    }
}
