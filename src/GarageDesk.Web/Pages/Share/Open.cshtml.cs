using System.Globalization;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace GarageDesk.Web.Pages.Share;

/// <summary>
/// The customer's page behind a share link: /s/{token}. No login, read only.
/// The URL carries one thing, the secret token. No shop id and no job or estimate id is ever taken from it:
/// the token is looked up first, and only the link's own stored shop and target are read.
/// An unknown, expired, revoked or broken link all answer with the same bare 404.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(ShareLinkSetup.RateLimitPolicy)]
public class OpenModel(PublicShareLoader loader) : PageModel
{
    public PublicShareView Content { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken ct)
    {
        // The middleware (ShareLinkSetup) has normally resolved the token already, to set the page language.
        var link = ShareLinkSetup.ResolvedLink(HttpContext) ?? await loader.FindLiveLinkAsync(token, ct);
        if (link is null)
        {
            return NotFound();
        }

        var content = await loader.LoadAsync(link, ct);
        if (content is null)
        {
            return NotFound();
        }

        // The owner previewing their own link while signed in should not inflate the customer's view count.
        var isOwnerPreview = User.FindFirst(CurrentShop.ClaimType)?.Value == link.ShopId.ToString(CultureInfo.InvariantCulture);
        if (!isOwnerPreview && !LinkPreviewBots.IsPreviewBot(Request.Headers.UserAgent.ToString()))
        {
            await loader.RecordViewAsync(link.Id, ct);
        }

        Content = content;
        return Page();
    }

    /// <summary>The shop logo for this link (?handler=logo). Same token rules as the page; nothing else is exposed.</summary>
    public async Task<IActionResult> OnGetLogoAsync(string token, CancellationToken ct)
    {
        var link = ShareLinkSetup.ResolvedLink(HttpContext) ?? await loader.FindLiveLinkAsync(token, ct);
        if (link is null)
        {
            return NotFound();
        }

        var logo = await loader.LoadLogoAsync(link, ct);
        if (logo is null)
        {
            return NotFound();
        }

        // Uploaded content: never let a browser guess another type or run anything from this response.
        Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";
        return File(logo.Value.Bytes, logo.Value.ContentType);
    }
}
