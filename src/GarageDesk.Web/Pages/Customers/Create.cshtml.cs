using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Services;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Customers;

[Authorize(Policy = Policies.Staff)]
public class CreateModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : CustomerFormPageModel(db, localizer)
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var phone = NormalizePhoneOrAddError();
        if (!ModelState.IsValid || phone is null)
        {
            return Page();
        }

        var customer = new Customer
        {
            Name = Input.Name.Trim(),
            Phone = phone,
            Notes = TrimToNull(Input.Notes),
        };
        Db.Customers.Add(customer); // ShopId is filled in by the DbContext
        await Db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = customer.Id });
    }
}
