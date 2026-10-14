using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Reminders;

public class EditModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : ReminderFormPageModel(db, localizer)
{
    public int ReminderId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's reminder id finds nothing (query filter), so it is a plain 404.
        var reminder = await Db.Reminders.AsNoTracking()
            .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null)
        {
            return NotFound();
        }

        ReminderId = reminder.Id;
        Vehicle = reminder.Vehicle;
        Input.VehicleId = reminder.VehicleId;
        Input.Type = reminder.Type;
        Input.DueDate = reminder.DueDate;
        Input.DueMileage = reminder.DueMileage;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var reminder = await Db.Reminders
            .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null)
        {
            return NotFound();
        }

        ReminderId = reminder.Id;
        Vehicle = reminder.Vehicle;
        Input.VehicleId = reminder.VehicleId; // the vehicle of a reminder is fixed

        ValidateDue();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        reminder.Type = Input.Type.Trim();
        reminder.DueDate = Input.DueDate;
        reminder.DueMileage = Input.DueMileage;
        await Db.SaveChangesAsync();

        return RedirectToPage("Index");
    }
}
