using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Customers;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 100;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<Customer> Customers { get; private set; } = [];

    public bool HasMore { get; private set; }

    /// <summary>The search text as a plate key, so the view can highlight the plate that matched.</summary>
    public string PlateKey => PlateNormalizer.Key(Q);

    public async Task OnGetAsync()
    {
        var found = await db.Customers
            .AsNoTracking()
            .Search(Q)
            .Include(c => c.Vehicles.OrderBy(v => v.Plate))
            .OrderBy(c => c.Name)
            .Take(PageSize + 1)
            .ToListAsync();

        HasMore = found.Count > PageSize;
        Customers = found.Take(PageSize).ToList();
    }
}
