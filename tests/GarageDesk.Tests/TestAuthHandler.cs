using System.Security.Claims;
using System.Text.Encodings.Web;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarageDesk.Tests;

/// <summary>
/// Lets tests act as a signed-in user without going through the login form:
/// send X-Test-User (and optionally X-Test-Shop) headers. No header means anonymous.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string ShopHeader = "X-Test-Shop";
    public const string ShopNameHeader = "X-Test-Shop-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "owner@example.com"),
        };
        if (Request.Headers.TryGetValue(ShopHeader, out var shopId))
        {
            claims.Add(new Claim(CurrentShop.ClaimType, shopId.ToString()));
        }
        if (Request.Headers.TryGetValue(ShopNameHeader, out var shopName))
        {
            claims.Add(new Claim(CurrentShop.NameClaimType, shopName.ToString()));
        }

        // Same authentication type Identity uses, so SignInManager.IsSignedIn() is true in the layout.
        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
