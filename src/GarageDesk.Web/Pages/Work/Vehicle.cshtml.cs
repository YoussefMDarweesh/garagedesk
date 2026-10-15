using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Work;

public sealed record VisitLine(JobLineType Type, string Description, decimal Quantity);

/// <summary>One past visit: what was done, with no prices or totals.</summary>
public sealed record Visit(int JobId, DateOnly DateIn, int MileageAtVisit, JobStatus Status, string? Notes, bool CanOpen, List<VisitLine> Lines);

/// <summary>
/// A vehicle's service history for a technician: every visit and what was done, no money and no phone number.
/// A technician can open the vehicles of the jobs assigned to them.
/// </summary>
[Authorize(Policy = Policies.Team)]
public class VehicleModel(ApplicationDbContext db, ICurrentStaff currentStaff) : PageModel
{
    public int Id { get; private set; }

    public string Plate { get; private set; } = "";

    public string Make { get; private set; } = "";

    public string ModelName { get; private set; } = "";

    public int Year { get; private set; }

    public int Mileage { get; private set; }

    public string? Vin { get; private set; }

    public string? OwnerName { get; private set; }

    public List<Visit> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's vehicle finds nothing (query filter): 404.
        var vehicle = await db.Vehicles.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new { v.Id, v.Plate, v.Make, v.Model, v.Year, v.Mileage, v.Vin, Owner = v.Customer!.Name })
            .FirstOrDefaultAsync();
        if (vehicle is null)
        {
            return NotFound();
        }

        var staff = (await currentStaff.GetAsync())!;
        var visits = await db.JobCards.AsNoTracking()
            .Where(j => j.VehicleId == id)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Select(j => new
            {
                j.Id,
                j.DateIn,
                j.MileageAtVisit,
                j.Status,
                j.Notes,
                j.AssignedToUserId,
                Lines = j.Lines.OrderBy(l => l.Id).Select(l => new VisitLine(l.Type, l.Description, l.Quantity)).ToList(),
            })
            .ToListAsync();

        // A technician reaches a vehicle through a job assigned to them, never by browsing.
        if (staff.IsTechnician && visits.All(v => v.AssignedToUserId != staff.UserId))
        {
            return Forbid();
        }

        Id = vehicle.Id;
        Plate = vehicle.Plate;
        Make = vehicle.Make;
        ModelName = vehicle.Model;
        Year = vehicle.Year;
        Mileage = vehicle.Mileage;
        Vin = vehicle.Vin;
        OwnerName = vehicle.Owner;
        History = visits
            .Select(v => new Visit(
                v.Id, v.DateIn, v.MileageAtVisit,
                v.Status == JobStatus.Paid ? JobStatus.Done : v.Status, // whether it was paid is not the technician's business
                v.Notes,
                CanOpen: !staff.IsTechnician || v.AssignedToUserId == staff.UserId,
                v.Lines))
            .ToList();
        return Page();
    }
}
