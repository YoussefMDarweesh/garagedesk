using System.Security.Claims;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GarageDesk.Web.Services;

/// <summary>Adds the user's ShopId to the login cookie as a claim, so each request knows its shop without a DB hit.</summary>
public class AppUserClaimsPrincipalFactory(UserManager<AppUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.ShopId is int shopId)
        {
            identity.AddClaim(new Claim(CurrentShop.ClaimType, shopId.ToString()));
        }
        return identity;
    }
}
