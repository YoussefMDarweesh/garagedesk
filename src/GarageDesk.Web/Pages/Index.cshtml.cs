using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public string? ShopName { get; private set; }

    public async Task OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // The query filter limits Shops to the signed-in user's own shop.
            ShopName = await db.Shops.Select(s => s.Name).FirstOrDefaultAsync();
        }
    }
}
