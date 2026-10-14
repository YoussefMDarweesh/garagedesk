using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Work;

/// <summary>One row of "My jobs". Deliberately has no money fields at all: there is nothing to leak.</summary>
public sealed record WorkRow(
    int Id, DateOnly DateIn, JobStatus Status, string Plate, string Make, string Model, string? OwnerName, int LineCount, int VehicleId);

/// <summary>
/// "My jobs": the job cards assigned to the signed-in person, with no prices, totals, payments or phone numbers.
/// This is a technician's whole home page. Nothing on it (or in its query) can carry money.
/// </summary>
[Authorize(Policy = Policies.Team)]
public class IndexModel(ApplicationDbContext db, ICurrentStaff currentStaff) : PageModel
{
    /// <summary>"all" shows finished jobs too. The default is the open ones: Booked, In progress, Done.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    public bool ShowAll => string.Equals(Show, "all", StringComparison.OrdinalIgnoreCase);

    public List<WorkRow> Jobs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var me = (await currentStaff.GetAsync())!.UserId;

        var query = db.JobCards.AsNoTracking().Where(j => j.AssignedToUserId == me);
        if (!ShowAll)
        {
            // A paid job is finished business; it is not "open work" any more.
            query = query.Where(j => j.Status != JobStatus.Paid);
        }

        // Only these columns are read: no prices, no payments, no phone number.
        Jobs = await query
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Select(j => new WorkRow(
                j.Id, j.DateIn, j.Status, j.Vehicle!.Plate, j.Vehicle.Make, j.Vehicle.Model,
                j.Vehicle.Customer!.Name, j.Lines.Count, j.VehicleId))
            .Take(200)
            .ToListAsync();
    }
}
