using System.Security.Claims;
using System.Text.Encodings.Web;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GarageDesk.Web.Services;

/// <summary>
/// The names of the authorization policies. Every Razor Page declares one of them with
/// [Authorize(Policy = ...)] (or is explicitly [AllowAnonymous]); a test enforces that.
/// </summary>
public static class Policies
{
    /// <summary>Any signed-in, active user (also one who has no shop yet).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Owners only: shop settings, staff, commissions.</summary>
    public const string Owner = "Owner";

    /// <summary>Owners and cashiers: customers, vehicles, job cards with prices, payments, invoices, estimates.</summary>
    public const string Staff = "Staff";

    /// <summary>Anyone in the shop, technicians included: the pages that never show money ("My jobs").</summary>
    public const string Team = "Team";

    public static void Register(AuthorizationOptions options)
    {
        // Plain [Authorize] means "signed in and still active", never just "has a cookie".
        var signedIn = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveMemberRequirement())
            .Build();
        options.DefaultPolicy = signedIn;
        options.AddPolicy(SignedIn, signedIn);

        options.AddPolicy(Owner, p => p.RequireAuthenticatedUser().AddRequirements(
            new ActiveMemberRequirement(), new ShopRoleRequirement(ShopRole.Owner)));
        options.AddPolicy(Staff, p => p.RequireAuthenticatedUser().AddRequirements(
            new ActiveMemberRequirement(), new ShopRoleRequirement(ShopRole.Owner, ShopRole.Cashier)));
        options.AddPolicy(Team, p => p.RequireAuthenticatedUser().AddRequirements(
            new ActiveMemberRequirement(), new ShopRoleRequirement(ShopRole.Owner, ShopRole.Cashier, ShopRole.Technician)));
    }
}

/// <summary>The signed-in user as the database knows them right now (not as an old cookie remembers them).</summary>
public sealed record StaffContext(string UserId, int? ShopId, ShopRole Role, bool IsActive, string DisplayName)
{
    public bool IsOwner => Role == ShopRole.Owner;

    public bool IsTechnician => Role == ShopRole.Technician;

    /// <summary>Owners and cashiers handle money; technicians never see it.</summary>
    public bool CanSeeMoney => Role is ShopRole.Owner or ShopRole.Cashier;
}

/// <summary>
/// Who is making this request. The role and the active flag are read from the database once per request, so
/// deactivating someone or changing their role takes effect immediately, whatever their login cookie says.
/// </summary>
public interface ICurrentStaff
{
    /// <summary>Null when nobody is signed in, the user no longer exists, or the cookie's shop is not the user's shop.</summary>
    Task<StaffContext?> GetAsync();
}

public sealed class CurrentStaff(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db) : ICurrentStaff
{
    private Task<StaffContext?>? _loaded;

    public Task<StaffContext?> GetAsync() => _loaded ??= LoadAsync();

    private async Task<StaffContext?> LoadAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.ShopId, u.Role, u.IsActive, u.FullName, u.Email, u.UserName })
            .FirstOrDefaultAsync();
        if (user is null)
        {
            return null;
        }

        // The shop travels in the cookie as a claim. If it ever disagrees with the database, trust neither.
        var claimShop = int.TryParse(principal.FindFirstValue(CurrentShop.ClaimType), out var id) ? id : (int?)null;
        if (claimShop != user.ShopId && claimShop is not null)
        {
            return null;
        }

        var name = !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : (user.Email ?? user.UserName ?? "").Split('@')[0];
        return new StaffContext(userId, claimShop, user.Role, user.IsActive, name);
    }
}

/// <summary>The user must exist and be active. Added to every policy, so a deactivated person is stopped everywhere.</summary>
public sealed class ActiveMemberRequirement : IAuthorizationRequirement;

/// <summary>The user's role must be one of these.</summary>
public sealed class ShopRoleRequirement(params ShopRole[] allowed) : IAuthorizationRequirement
{
    public IReadOnlyList<ShopRole> Allowed { get; } = allowed;
}

public sealed class ActiveMemberHandler(ICurrentStaff currentStaff) : AuthorizationHandler<ActiveMemberRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveMemberRequirement requirement)
    {
        var staff = await currentStaff.GetAsync();
        if (staff is { IsActive: true })
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class ShopRoleHandler(ICurrentStaff currentStaff) : AuthorizationHandler<ShopRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ShopRoleRequirement requirement)
    {
        var staff = await currentStaff.GetAsync();
        if (staff is not { IsActive: true })
        {
            return;
        }

        // Someone who has not created their shop yet has no role anywhere. They pass here only so that
        // RequireShopPageFilter can send them to /CreateShop; with no shop every query returns nothing.
        if (staff.ShopId is null || requirement.Allowed.Contains(staff.Role))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>A deactivated user cannot sign in, so they cannot even get a new cookie.</summary>
public sealed class GarageSignInManager(
    UserManager<AppUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<AppUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<AppUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation)
    : SignInManager<AppUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(AppUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}

/// <summary>
/// What an authenticated user gets when a policy says no: a real 403 with a small page, never a 404 and
/// never a redirect to a login page (they are logged in; they just may not do this).
/// </summary>
public static class ForbiddenResponse
{
    public static async Task WriteAsync(HttpContext context)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status403Forbidden;
        response.Headers.CacheControl = "no-store";
        response.ContentType = "text/html; charset=utf-8";

        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var encoder = HtmlEncoder.Default;
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var dir = Cultures.IsRightToLeft(lang) ? "rtl" : "ltr";
        var basePath = context.Request.PathBase.Value ?? "";

        await response.WriteAsync($$"""
            <!DOCTYPE html>
            <html lang="{{encoder.Encode(lang)}}" dir="{{dir}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="robots" content="noindex" />
              <title>{{encoder.Encode(localizer["Access denied"].Value)}} · GarageDesk</title>
              <link rel="stylesheet" href="{{basePath}}/lib/bootstrap/dist/css/{{(dir == "rtl" ? "bootstrap.rtl.min.css" : "bootstrap.min.css")}}" />
              <link rel="stylesheet" href="{{basePath}}/css/site.css" />
            </head>
            <body class="public">
              <main class="public-main">
                <section class="panel panel-pad" style="max-width: 36rem; margin-inline: auto">
                  <h1 class="h3 fw-bold">{{encoder.Encode(localizer["Access denied"].Value)}}</h1>
                  <p class="text-secondary">{{encoder.Encode(localizer["Your role does not allow you to open this page. Ask the shop owner if you need access."].Value)}}</p>
                  <a class="btn btn-primary" href="{{basePath}}/">{{encoder.Encode(localizer["Back to home"].Value)}}</a>
                </section>
              </main>
            </body>
            </html>
            """);
    }
}
