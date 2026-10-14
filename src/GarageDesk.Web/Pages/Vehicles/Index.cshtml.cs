using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 100;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<Vehicle> Vehicles { get; private set; } = [];

    public bool HasMore { get; private set; }

    public async Task OnGetAsync()
    {
        var found = await db.Vehicles
            .AsNoTracking()
            .Search(Q)
            .Include(v => v.Customer)
            .OrderBy(v => v.Plate)
            .Take(PageSize + 1)
            .ToListAsync();

        HasMore = found.Count > PageSize;
        Vehicles = found.Take(PageSize).ToList();
    }
}
