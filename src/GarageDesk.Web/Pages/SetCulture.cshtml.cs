using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GarageDesk.Web.Pages;

/// <summary>Target of the navbar language switcher: remembers the chosen language in a cookie, then goes back.</summary>
public class SetCultureModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public IActionResult OnPost(string? culture, string? returnUrl)
    {
        if (culture is not null && Cultures.Supported.Contains(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                });
        }

        // Only ever go back to a page on this site (prevents open redirects).
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
