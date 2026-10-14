using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Customers;

public class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Customer Customer { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .Include(c => c.Vehicles.OrderBy(v => v.Plate))
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer is null)
        {
            return NotFound();
        }

        Customer = customer;
        return Page();
    }
}
