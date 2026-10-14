using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Vehicles;

public class EditModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : VehicleFormPageModel(db, localizer)
{
    public int VehicleId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's vehicle id finds nothing (query filter), so it is a plain 404.
        var vehicle = await Db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null)
        {
            return NotFound();
        }

        VehicleId = vehicle.Id;
        Input.CustomerId = vehicle.CustomerId;
        Input.Plate = vehicle.Plate;
        Input.Vin = vehicle.Vin;
        Input.Make = vehicle.Make;
        Input.Model = vehicle.Model;
        Input.Year = vehicle.Year;
        Input.Mileage = vehicle.Mileage;
        await LoadOwnersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var vehicle = await Db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null)
        {
            return NotFound();
        }

        VehicleId = vehicle.Id;
        if (!await ValidateOwnerAndPlateAsync())
        {
            await LoadOwnersAsync();
            return Page();
        }

        CopyInputTo(vehicle);
        await Db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = vehicle.Id });
    }
}
