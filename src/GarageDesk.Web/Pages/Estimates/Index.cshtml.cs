using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Services;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Estimates;

[Authorize(Policy = Policies.Staff)]
public class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<Estimate> Estimates { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Estimates = await db.Estimates.AsNoTracking()
            .Include(e => e.Vehicle).ThenInclude(v => v!.Customer)
            .Include(e => e.Lines)
            .OrderByDescending(e => e.Number)
            .Take(200)
            .ToListAsync();
    }
}
