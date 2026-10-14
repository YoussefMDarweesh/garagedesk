using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class DetailsModel(ApplicationDbContext db) : PageModel
{
    /// <summary>The service interval the status tiles measure against, in kilometres (the oil-change suggestion).</summary>
    public const int ServiceIntervalKm = Reminder.SuggestedOilChangeKm;

    /// <summary>...and in days.</summary>
    public const int ServiceIntervalDays = Reminder.SuggestedOilChangeMonths * 30;

    public Vehicle Vehicle { get; private set; } = null!;

    /// <summary>Every job card of this vehicle, newest visit first: its service history.</summary>
    public List<JobCard> History { get; private set; } = [];

    /// <summary>This vehicle's reminders that are still open, most urgent first.</summary>
    public List<Reminder> OpenReminders { get; private set; } = [];

    public DateOnly Today { get; private set; }

    public string? ShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    /// <summary>What all the visits added up to.</summary>
    public decimal HistoryTotal => History.Sum(j => j.Total);

    /// <summary>The most recent job card that was actually carried out (Done or Paid). Null if there is none.</summary>
    public JobCard? LastService => History.FirstOrDefault(j => j.Status is JobStatus.Done or JobStatus.Paid);

    /// <summary>Kilometres driven since the last service, from the readings we already store. Null if never serviced.</summary>
    public int? KmSinceService => LastService is null ? null : Math.Max(0, Vehicle.Mileage - LastService.MileageAtVisit);

    public int? DaysSinceService => LastService is null ? null : Math.Max(0, Today.DayNumber - LastService.DateIn.DayNumber);

    public int OverdueReminderCount => OpenReminders.Count(r => r.StatusFor(Today, Vehicle.Mileage) == ReminderStatus.Overdue);

    /// <summary>How full a tile's progress bar is (0 to 100) and how it is coloured.</summary>
    public static (int Percent, string Tone) Meter(int value, int interval)
    {
        var percent = (int)Math.Min(100, Math.Round(value * 100.0 / interval));
        var tone = value >= interval ? "bad" : value >= interval * 0.8 ? "warn" : "";
        return (percent, tone);
    }

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

        Today = ShopClock.Today();

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
