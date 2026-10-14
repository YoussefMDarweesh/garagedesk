using System.Security.Claims;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageDesk.Web.Services;

/// <summary>
/// Adds the user's shop (id and name) to the login cookie as claims, so each request knows its shop
/// without a database call.
/// </summary>
public class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    IOptions<IdentityOptions> options,
    ApplicationDbContext db) : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.ShopId is int shopId)
        {
            identity.AddClaim(new Claim(CurrentShop.ClaimType, shopId.ToString()));

            // This runs while the cookie is being built, before the shop_id claim exists, so the shop
            // query filter would hide the row. We read exactly the user's own shop, nothing else.
            var shop = await db.Shops.IgnoreQueryFilters()
                .Where(s => s.Id == shopId)
                .Select(s => new { s.Name, s.AccentColor })
                .FirstOrDefaultAsync();
            if (shop is not null)
            {
                identity.AddClaim(new Claim(CurrentShop.NameClaimType, shop.Name));

                // Only a known preset is ever carried; the default needs no claim.
                if (Accents.IsPreset(shop.AccentColor) && shop.AccentColor != Accents.Default)
                {
                    identity.AddClaim(new Claim(CurrentShop.AccentClaimType, shop.AccentColor!));
                }
            }

            // Same reasoning: only a "has a logo, and which version" marker, never the image itself.
            var logoUpdated = await db.ShopLogos.IgnoreQueryFilters()
                .Where(l => l.ShopId == shopId)
                .Select(l => (DateTime?)l.UpdatedAtUtc)
                .FirstOrDefaultAsync();
            if (logoUpdated is { } updated)
            {
                identity.AddClaim(new Claim(CurrentShop.LogoClaimType, updated.Ticks.ToString()));
            }
        }
        return identity;
    }
}
