using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages;

// Signed in, and (via RequireShopPageFilter) a shop must exist. All reads and writes below are
// automatically limited to the current shop by the DbContext's query filter.
[Authorize]
public class ShopSettingsModel(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public bool HasLogo { get; private set; }

    public async Task OnGetAsync()
    {
        HasLogo = await db.ShopLogos.AnyAsync();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? upload)
    {
        if (upload is null || upload.Length == 0)
        {
            return await RejectAsync("Choose an image to upload.");
        }

        if (upload.Length > ShopLogoImage.MaxBytes)
        {
            return await RejectAsync("The image is too large. The maximum size is 512 KB.");
        }

        var bytes = new byte[upload.Length];
        await using (var stream = upload.OpenReadStream())
        {
            await stream.ReadExactlyAsync(bytes);
        }

        // Judge the file by its content. The name and the content type sent by the browser can be faked.
        var contentType = ShopLogoImage.Detect(bytes);
        if (contentType is null)
        {
            return await RejectAsync("Only PNG, JPEG or WebP images are allowed.");
        }

        var logo = await db.ShopLogos.FirstOrDefaultAsync();
        if (logo is null)
        {
            logo = new ShopLogo(); // ShopId is filled in by the DbContext
            db.ShopLogos.Add(logo);
        }

        logo.Bytes = bytes;
        logo.ContentType = contentType;
        logo.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await RefreshLoginAsync();
        StatusMessage = localizer["Logo updated."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync()
    {
        var logo = await db.ShopLogos.FirstOrDefaultAsync();
        if (logo is not null)
        {
            db.ShopLogos.Remove(logo);
            await db.SaveChangesAsync();
            await RefreshLoginAsync();
        }

        StatusMessage = localizer["Logo removed."];
        return RedirectToPage();
    }

    private async Task<IActionResult> RejectAsync(string englishMessage)
    {
        ModelState.AddModelError(string.Empty, localizer[englishMessage]);
        HasLogo = await db.ShopLogos.AnyAsync();
        return Page();
    }

    // The navbar reads "has a logo" from the login cookie, so re-issue the cookie after a change.
    private async Task RefreshLoginAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is not null)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }
}
