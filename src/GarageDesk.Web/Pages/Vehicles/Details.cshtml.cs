using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Vehicle Vehicle { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle is null)
        {
            return NotFound();
        }

        Vehicle = vehicle;
        return Page();
    }
}
