using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Vehicle Vehicle { get; private set; } = null!;

    /// <summary>Every job card of this vehicle, newest visit first: its service history.</summary>
    public List<JobCard> History { get; private set; } = [];

    /// <summary>This vehicle's reminders that are still open, most urgent first.</summary>
    public List<Reminder> OpenReminders { get; private set; } = [];

    public DateOnly Today { get; private set; }

    public string? ShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    /// <summary>What all the visits added up to.</summary>
    public decimal HistoryTotal => History.Sum(j => j.Total);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's vehicle id finds nothing (query filter), so it is a plain 404.
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle is null)
        {
            return NotFound();
        }

        // Totals are worked out in memory from the lines (see JobCard.Total), so the lines come along.
        // Same date? The later job card (higher id) is shown first.
        History = await db.JobCards
            .AsNoTracking()
            .Where(j => j.VehicleId == id)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .ToListAsync();

        foreach (var job in History)
        {
            job.Lines = job.Lines.OrderBy(l => l.Id).ToList();
        }

        Today = ShopClock.Today();
        var reminders = await db.Reminders
            .AsNoTracking()
            .Where(r => r.VehicleId == id && !r.IsDone)
            .ToListAsync();
        foreach (var reminder in reminders)
        {
            reminder.Vehicle = vehicle; // already loaded with its owner; the row needs both
        }
        OpenReminders = reminders
            .OrderByDescending(r => r.StatusFor(Today, vehicle.Mileage))
            .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.DueMileage ?? int.MaxValue)
            .ToList();

        Vehicle = vehicle;
        return Page();
    }
}
