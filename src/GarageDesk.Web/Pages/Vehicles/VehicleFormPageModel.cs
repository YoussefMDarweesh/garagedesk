using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Vehicles;

/// <summary>What the Create and Edit vehicle pages share: the form model, the owner list and the checks.</summary>
public abstract class VehicleFormPageModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    protected ApplicationDbContext Db => db;

    [BindProperty]
    public VehicleInput Input { get; set; } = new();

    public List<SelectListItem> Owners { get; private set; } = [];

    // Reads go through the DbContext's query filter, so only this shop's customers can ever be listed or chosen.
    protected async Task LoadOwnersAsync()
    {
        Owners = await Db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name + " (" + c.Phone + ")", c.Id.ToString()))
            .ToListAsync();
    }

    /// <summary>Checks the posted owner and plate. Adds validation errors and returns false if something is wrong.</summary>
    protected async Task<bool> ValidateOwnerAndPlateAsync()
    {
        // A forged CustomerId pointing at another shop's customer is not found here: the filter hides it.
        var ownerExists = Input.CustomerId > 0 && await Db.Customers.AnyAsync(c => c.Id == Input.CustomerId);
        if (!ownerExists)
        {
            ModelState.AddModelError("Input.CustomerId", localizer["Choose the owner."]);
        }

        if (!string.IsNullOrWhiteSpace(Input.Plate) && PlateNormalizer.Key(Input.Plate).Length == 0)
        {
            ModelState.AddModelError("Input.Plate", localizer["Enter a valid plate number."]);
        }

        return ModelState.IsValid;
    }

    protected void CopyInputTo(Vehicle vehicle)
    {
        vehicle.CustomerId = Input.CustomerId;
        vehicle.Plate = Input.Plate.Trim();
        vehicle.PlateKey = PlateNormalizer.Key(Input.Plate);
        vehicle.Vin = string.IsNullOrWhiteSpace(Input.Vin) ? null : Input.Vin.Trim().ToUpperInvariant();
        vehicle.Make = Input.Make.Trim();
        vehicle.Model = Input.Model.Trim();
        vehicle.Year = Input.Year;
        vehicle.Mileage = Input.Mileage;
    }
}
