using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Vehicles;

public class CreateModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : VehicleFormPageModel(db, localizer)
{
    public async Task OnGetAsync(int? customerId)
    {
        // Arriving from a customer's page pre-selects that customer as the owner.
        Input.CustomerId = customerId ?? 0;
        Input.Year = DateTime.UtcNow.Year;
        await LoadOwnersAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await ValidateOwnerAndPlateAsync())
        {
            await LoadOwnersAsync();
            return Page();
        }

        var vehicle = new Vehicle();
        CopyInputTo(vehicle);
        Db.Vehicles.Add(vehicle); // ShopId is filled in by the DbContext
        await Db.SaveChangesAsync();

        // Back to the owner, so a second car can be added straight away.
        return RedirectToPage("/Customers/Details", new { id = vehicle.CustomerId });
    }
}
