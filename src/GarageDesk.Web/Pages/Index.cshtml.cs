using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public string? ShopName { get; private set; }

    public int CustomerCount { get; private set; }

    public int VehicleCount { get; private set; }

    public int JobCount { get; private set; }

    /// <summary>Job cards that are not paid yet.</summary>
    public int OpenJobCount { get; private set; }

    public async Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // The query filter limits all of these to the signed-in user's own shop.
            ShopName = await db.Shops.Select(s => s.Name).FirstOrDefaultAsync();
            CustomerCount = await db.Customers.CountAsync();
            VehicleCount = await db.Vehicles.CountAsync();
            JobCount = await db.JobCards.CountAsync();
            OpenJobCount = await db.JobCards.CountAsync(j => j.Status != JobStatus.Paid);
        }
    }
}
