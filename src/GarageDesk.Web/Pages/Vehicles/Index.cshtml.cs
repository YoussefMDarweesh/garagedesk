using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Vehicles;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<Vehicle> Vehicles { get; private set; } = [];

    public int Total { get; private set; }

    public int PageCount { get; private set; } = 1;

    public PagerModel Pager => new(P, PageCount, Total, PageSize, "/Vehicles/Index",
        new Dictionary<string, string?> { ["q"] = Q });

    public async Task OnGetAsync()
    {
        var query = db.Vehicles.AsNoTracking().Search(Q);

        Total = await query.CountAsync();
        PageCount = PagerModel.CountPages(Total, PageSize);
        P = Math.Clamp(P, 1, PageCount);

        Vehicles = await query
            .Include(v => v.Customer)
            .OrderBy(v => v.Plate).ThenBy(v => v.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
