using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Customers;

public class EditModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : CustomerFormPageModel(db, localizer)
{
    public int CustomerId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Another shop's customer id finds nothing (query filter), so it is a plain 404.
        var customer = await Db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null)
        {
            return NotFound();
        }

        CustomerId = customer.Id;
        Input.Name = customer.Name;
        Input.Phone = customer.Phone;
        Input.Notes = customer.Notes;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var customer = await Db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null)
        {
            return NotFound();
        }

        CustomerId = customer.Id;
        var phone = NormalizePhoneOrAddError();
        if (!ModelState.IsValid || phone is null)
        {
            return Page();
        }

        customer.Name = Input.Name.Trim();
        customer.Phone = phone;
        customer.Notes = TrimToNull(Input.Notes);
        await Db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = customer.Id });
    }
}
