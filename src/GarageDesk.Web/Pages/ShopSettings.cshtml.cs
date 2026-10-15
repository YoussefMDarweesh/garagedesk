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
[Authorize(Policy = Policies.Owner)]
public class ShopSettingsModel(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public bool HasLogo { get; private set; }

    /// <summary>The shop's accent colour preset (Accents.Default when none was chosen).</summary>
    public string Accent { get; private set; } = Accents.Default;

    public decimal? VatPercent { get; private set; }

    /// <summary>The shop's own WhatsApp message in English and in Arabic (empty: the built-in wording is used).</summary>
    public string? ShareMessageEn { get; private set; }

    public string? ShareMessageAr { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        HasLogo = await db.ShopLogos.AnyAsync();
        var accent = await db.Shops.Select(s => s.AccentColor).FirstOrDefaultAsync();
        Accent = Accents.IsPreset(accent) ? accent! : Accents.Default;
        VatPercent = await db.Shops.Select(s => s.VatPercent).FirstOrDefaultAsync();
        ShareMessageEn = await db.Shops.Select(s => s.ShareMessageEn).FirstOrDefaultAsync();
        ShareMessageAr = await db.Shops.Select(s => s.ShareMessageAr).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Saves the shop's WhatsApp message wording. Placeholders are {name}, {plate}, {shop} and {link}; they are
    /// replaced by plain text only (see ShareMessage), never evaluated. A custom message must contain {link}.
    /// </summary>
    public async Task<IActionResult> OnPostShareMessageAsync(string? shareMessageEn, string? shareMessageAr)
    {
        var english = string.IsNullOrWhiteSpace(shareMessageEn) ? null : shareMessageEn.Trim();
        var arabic = string.IsNullOrWhiteSpace(shareMessageAr) ? null : shareMessageAr.Trim();

        foreach (var text in new[] { english, arabic })
        {
            if (text is not null && !ShareMessage.IsAcceptable(text))
            {
                await LoadAsync();
                ShareMessageEn = english;
                ShareMessageAr = arabic;
                ModelState.AddModelError("shareMessage", localizer["The message must include {link} and be at most 500 characters."]);
                return Page();
            }
        }

        var shop = await db.Shops.FirstOrDefaultAsync();
        if (shop is null)
        {
            return NotFound();
        }

        shop.ShareMessageEn = english;
        shop.ShareMessageAr = arabic;
        await db.SaveChangesAsync();

        StatusMessage = localizer["WhatsApp message saved."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostVatAsync(decimal? vatPercent)
    {
        if (vatPercent is < 0 or > 100)
        {
            await LoadAsync();
            ModelState.AddModelError(string.Empty, localizer["Enter a VAT percent between 0 and 100."]);
            return Page();
        }

        var shop = await db.Shops.FirstOrDefaultAsync();
        if (shop is null)
        {
            return NotFound();
        }

        shop.VatPercent = vatPercent is > 0 ? Math.Round(vatPercent.Value, 2) : null;
        await db.SaveChangesAsync();

        StatusMessage = localizer["VAT setting saved."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAccentAsync(string? accent)
    {
        // Only one of the known presets is accepted; anything else is ignored.
        if (!Accents.IsPreset(accent))
        {
            await LoadAsync();
            ModelState.AddModelError(string.Empty, localizer["Choose one of the colours."]);
            return Page();
        }

        var shop = await db.Shops.FirstOrDefaultAsync(); // the query filter limits this to the signed-in user's shop
        if (shop is null)
        {
            return NotFound();
        }

        shop.AccentColor = accent == Accents.Default ? null : accent;
        await db.SaveChangesAsync();

        await RefreshLoginAsync();
        StatusMessage = localizer["Accent colour updated."];
        return RedirectToPage();
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
