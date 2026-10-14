using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GarageDesk.Web.Services;

public static class ShopLogoEndpoint
{
    public const string Path = "/shop/logo";

    /// <summary>
    /// GET /shop/logo returns the signed-in user's own shop logo. There is deliberately no shop id in the URL:
    /// the shop comes from the login, and the DbContext's query filter makes every other shop's logo invisible.
    /// </summary>
    public static void MapShopLogo(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (ApplicationDbContext db, HttpResponse response) =>
        {
            var logo = await db.Set<ShopLogo>()
                .AsNoTracking()
                .Select(l => new { l.Bytes, l.ContentType, l.UpdatedAtUtc })
                .FirstOrDefaultAsync();

            if (logo is null)
            {
                return Results.NotFound();
            }

            // Defence in depth for user-uploaded content: never let a browser guess a different type,
            // and never let this response run anything even if it were opened as a document.
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";

            // Per-user content, so no shared caches. The page adds ?v=<version> to the URL, so a changed
            // logo is fetched fresh while an unchanged one comes from the browser's cache.
            response.Headers.CacheControl = "private, max-age=86400";
            response.Headers.Vary = "Cookie";

            var updated = DateTime.SpecifyKind(logo.UpdatedAtUtc, DateTimeKind.Utc);
            return Results.File(
                logo.Bytes,
                logo.ContentType,
                lastModified: new DateTimeOffset(updated),
                entityTag: new EntityTagHeaderValue($"\"{updated.Ticks}\""));
        })
        .RequireAuthorization();
    }
}
