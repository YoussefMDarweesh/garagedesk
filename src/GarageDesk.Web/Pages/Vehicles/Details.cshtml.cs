using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Vehicle Vehicle { get; private set; } = null!;

    /// <summary>Every job card of this vehicle, newest visit first: its service history.</summary>
    public List<JobCard> History { get; private set; } = [];

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

        Vehicle = vehicle;
        return Page();
    }
}
