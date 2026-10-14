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
            var shopName = await db.Shops.IgnoreQueryFilters()
                .Where(s => s.Id == shopId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
            if (shopName is not null)
            {
                identity.AddClaim(new Claim(CurrentShop.NameClaimType, shopName));
            }
        }
        return identity;
    }
}
