using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Reminders;

/// <summary>What the Add and Edit reminder pages share: the form model, the vehicle list and the checks.</summary>
public abstract class ReminderFormPageModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    protected ApplicationDbContext Db => db;

    protected IStringLocalizer<SharedResource> Localizer => localizer;

    [BindProperty]
    public ReminderInput Input { get; set; } = new();

    /// <summary>Set when the vehicle is fixed (opened from a vehicle's page, or editing).</summary>
    public Vehicle? Vehicle { get; protected set; }

    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    protected async Task LoadVehicleOptionsAsync()
    {
        var vehicles = await Db.Vehicles
            .AsNoTracking()
            .OrderBy(v => v.Plate)
            .Select(v => new { v.Id, v.Plate, v.Make, v.Model, Owner = v.Customer!.Name })
            .Take(500)
            .ToListAsync();

        VehicleOptions = vehicles
            .Select(v => new SelectListItem($"{v.Plate} · {v.Make} {v.Model} ({v.Owner})", v.Id.ToString()))
            .ToList();
    }

    /// <summary>Adds the form-level errors this form needs; the caller then checks ModelState.IsValid.</summary>
    protected void ValidateDue()
    {
        if (Input.DueDate is null && Input.DueMileage is null)
        {
            ModelState.AddModelError(string.Empty, localizer["Enter a due date, a due mileage, or both."]);
        }

        if (Input.DueDate is { } date && date < new DateOnly(2000, 1, 1))
        {
            ModelState.AddModelError("Input.DueDate", localizer["Enter a valid date."]);
        }
    }
}
