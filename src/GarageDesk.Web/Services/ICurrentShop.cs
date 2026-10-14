using System.Security.Claims;

namespace GarageDesk.Web.Services;

/// <summary>The shop of the signed-in user, or null if nobody is signed in / the shop isn't created yet.</summary>
public interface ICurrentShop
{
    int? ShopId { get; }
}

/// <summary>Reads the shop id from the "shop_id" claim of the current request's user.</summary>
public class CurrentShop(IHttpContextAccessor httpContextAccessor) : ICurrentShop
{
    public const string ClaimType = "shop_id";

    /// <summary>The shop's display name, kept in the cookie too so the navbar needs no database call.</summary>
    public const string NameClaimType = "shop_name";

    /// <summary>Present only when the shop has a logo. The value is a version (ticks of the last change) used to bust the browser cache.</summary>
    public const string LogoClaimType = "shop_logo";

    public int? ShopId =>
        int.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimType), out var id) ? id : null;
}
