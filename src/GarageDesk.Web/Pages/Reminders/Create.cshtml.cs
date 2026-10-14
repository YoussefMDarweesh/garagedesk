using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Reminders;

public class CreateModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : ReminderFormPageModel(db, localizer)
{
    /// <summary>The query values let another page pre-fill the form (e.g. "next oil change" after a job is done).</summary>
    public async Task<IActionResult> OnGetAsync(int? vehicleId, string? type, DateOnly? dueDate, int? dueMileage)
    {
        Input.Type = type?.Trim() ?? "";
        Input.DueDate = dueDate;
        Input.DueMileage = dueMileage;

        if (vehicleId is int id)
        {
            // Another shop's vehicle id finds nothing (query filter), so it is a plain 404.
            var vehicle = await Db.Vehicles.AsNoTracking().Include(v => v.Customer).FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle is null)
            {
                return NotFound();
            }

            Vehicle = vehicle;
            Input.VehicleId = vehicle.Id;
        }
        else
        {
            await LoadVehicleOptionsAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // A forged VehicleId pointing at another shop's vehicle is not found here: the filter hides it.
        var vehicle = Input.VehicleId > 0
            ? await Db.Vehicles.AsNoTracking().Include(v => v.Customer).FirstOrDefaultAsync(v => v.Id == Input.VehicleId)
            : null;

        if (vehicle is null)
        {
            ModelState.AddModelError("Input.VehicleId", Localizer["Choose the vehicle."]);
        }

        ValidateDue();

        if (!ModelState.IsValid)
        {
            Vehicle = vehicle;
            if (vehicle is null)
            {
                await LoadVehicleOptionsAsync();
            }
            return Page();
        }

        Db.Reminders.Add(new Reminder // ShopId is filled in by the DbContext
        {
            VehicleId = vehicle!.Id,
            Type = Input.Type.Trim(),
            DueDate = Input.DueDate,
            DueMileage = Input.DueMileage,
        });
        await Db.SaveChangesAsync();

        return RedirectToPage("Index");
    }
}
