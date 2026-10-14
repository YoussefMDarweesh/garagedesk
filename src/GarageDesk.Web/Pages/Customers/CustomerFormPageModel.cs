using GarageDesk.Web.Data;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Customers;

/// <summary>What the Create and Edit customer pages share: the form model and phone handling.</summary>
public abstract class CustomerFormPageModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    protected ApplicationDbContext Db => db;

    [BindProperty]
    public CustomerInput Input { get; set; } = new();

    /// <summary>Returns the phone in +20 form, or null after adding a validation error to the form.</summary>
    protected string? NormalizePhoneOrAddError()
    {
        if (EgyptianPhone.TryNormalize(Input.Phone, out var phone))
        {
            return phone;
        }

        // Don't pile a second message on top of "Phone is required".
        if (!ModelState.TryGetValue("Input.Phone", out var entry) || entry.Errors.Count == 0)
        {
            ModelState.AddModelError("Input.Phone", localizer["Enter a valid Egyptian phone number, for example 0101 234 5678."]);
        }
        return null;
    }

    protected static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
