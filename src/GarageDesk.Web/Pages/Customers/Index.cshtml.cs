using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Customers;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<Customer> Customers { get; private set; } = [];

    public int Total { get; private set; }

    public int PageCount { get; private set; } = 1;

    /// <summary>The search text as a plate key, so the view can highlight the plate that matched.</summary>
    public string PlateKey => PlateNormalizer.Key(Q);

    public PagerModel Pager => new(P, PageCount, Total, PageSize, "/Customers/Index",
        new Dictionary<string, string?> { ["q"] = Q });

    public async Task OnGetAsync()
    {
        var query = db.Customers.AsNoTracking().Search(Q);

        Total = await query.CountAsync();
        PageCount = PagerModel.CountPages(Total, PageSize);
        P = Math.Clamp(P, 1, PageCount);

        Customers = await query
            .Include(c => c.Vehicles.OrderBy(v => v.Plate))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
