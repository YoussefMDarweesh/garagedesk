using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Jobs;

public class CreateModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public JobInput Input { get; set; } = new();

    /// <summary>Set when the page was opened from a vehicle's page: the vehicle is fixed.</summary>
    public Vehicle? Vehicle { get; private set; }

    /// <summary>Otherwise the shop picks one of its own vehicles.</summary>
    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? vehicleId)
    {
        Input.DateIn = ShopClock.Today();

        if (vehicleId is int id)
        {
            // Another shop's vehicle id finds nothing (query filter), so it is a plain 404.
            var vehicle = await db.Vehicles.AsNoTracking().Include(v => v.Customer).FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle is null)
            {
                return NotFound();
            }

            Vehicle = vehicle;
            Input.VehicleId = vehicle.Id;
            Input.MileageAtVisit = vehicle.Mileage; // a sensible starting point; the shop reads the odometer
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
        // (Tracked on purpose: the visit's mileage reading may raise the vehicle's current mileage below.)
        var vehicle = Input.VehicleId > 0
            ? await db.Vehicles.Include(v => v.Customer).FirstOrDefaultAsync(v => v.Id == Input.VehicleId)
            : null;

        if (vehicle is null)
        {
            ModelState.AddModelError("Input.VehicleId", localizer["Choose the vehicle."]);
        }

        if (Input.DateIn < new DateOnly(2000, 1, 1))
        {
            ModelState.AddModelError("Input.DateIn", localizer["Enter a valid date."]);
        }

        if (!ModelState.IsValid)
        {
            Vehicle = vehicle;
            if (vehicle is null)
            {
                await LoadVehicleOptionsAsync();
            }
            return Page();
        }

        var job = new JobCard
        {
            VehicleId = vehicle!.Id,
            DateIn = Input.DateIn,
            MileageAtVisit = Input.MileageAtVisit,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            Status = JobStatus.Booked,
        };
        db.JobCards.Add(job); // ShopId is filled in by the DbContext

        // The odometer reading at this visit becomes the vehicle's current mileage if it is higher.
        vehicle.RaiseMileageTo(job.MileageAtVisit);

        await db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = job.Id });
    }

    private async Task LoadVehicleOptionsAsync()
    {
        var vehicles = await db.Vehicles
            .AsNoTracking()
            .OrderBy(v => v.Plate)
            .Select(v => new { v.Id, v.Plate, v.Make, v.Model, Owner = v.Customer!.Name })
            .Take(500)
            .ToListAsync();

        VehicleOptions = vehicles
            .Select(v => new SelectListItem($"{v.Plate} · {v.Make} {v.Model} ({v.Owner})", v.Id.ToString()))
            .ToList();
    }
}
