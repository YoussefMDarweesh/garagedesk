using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GarageDesk.Web.Pages;

[Authorize]
public class CreateShopModel(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ICurrentShop currentShop) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(100)]
        [Display(Name = "Shop name")]
        public string Name { get; set; } = "";

        [Required, StringLength(30)]
        [Display(Name = "Phone")]
        public string Phone { get; set; } = "";

        [Required, StringLength(200)]
        [Display(Name = "Address")]
        public string Address { get; set; } = "";
    }

    public IActionResult OnGet()
    {
        // One shop per owner for now: someone who already has a shop has nothing to do here.
        return currentShop.ShopId is null ? Page() : RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (currentShop.ShopId is not null)
        {
            return RedirectToPage("/Index");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Saving the user also inserts the new shop, in one transaction.
        user.Shop = new Shop { Name = Input.Name.Trim(), Phone = Input.Phone.Trim(), Address = Input.Address.Trim() };
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Re-issue the login cookie so it now carries the shop_id claim.
        await signInManager.RefreshSignInAsync(user);
        return RedirectToPage("/Index");
    }
}
