using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages;

/// <summary>The sign-up form a staff member sees when they open an invitation link.</summary>
public class JoinInput
{
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [StringLength(100)]
    [Display(Name = "Name")]
    public string? FullName { get; set; }

    [Required, StringLength(100, MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Opened by the person who was invited, with no login. The token in the URL is the only thing they hold, so:
/// unknown, expired, used and cancelled links all look the same (no oracle), the token is looked up by its hash,
/// the invitation is consumed atomically (it works once, even if two requests race), and the page is never cached.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(InviteRateLimit.Policy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class JoinModel(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public JoinInput Input { get; set; } = new();

    /// <summary>False means: show the one generic "this link does not work" message.</summary>
    public bool Valid { get; private set; }

    public bool AlreadySignedIn { get; private set; }

    public string ShopName { get; private set; } = "";

    public ShopRole Role { get; private set; }

    public string RoleLabel => Role switch
    {
        ShopRole.Owner => localizer["Owner"].Value,
        ShopRole.Cashier => localizer["Cashier"].Value,
        _ => localizer["Technician"].Value,
    };

    public async Task<IActionResult> OnGetAsync(string token)
    {
        Harden();
        if (User.Identity?.IsAuthenticated == true)
        {
            // Accepting would silently replace the current login. Make the person choose.
            AlreadySignedIn = true;
            return Page();
        }

        var invite = await FindPendingAsync(token);
        if (invite is null)
        {
            return Invalid();
        }

        Valid = true;
        await FillAsync(invite);
        Input.FullName = invite.FullName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string token)
    {
        Harden();
        if (User.Identity?.IsAuthenticated == true)
        {
            AlreadySignedIn = true;
            return Page();
        }

        var invite = await FindPendingAsync(token);
        if (invite is null)
        {
            return Invalid();
        }

        Valid = true;
        await FillAsync(invite);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Consume the invitation first, atomically: only one request can flip UsedAtUtc from null.
        // If anything below fails the transaction rolls back and the invitation is usable again.
        var now = DateTime.UtcNow;
        var claimed = await db.StaffInvites.IgnoreQueryFilters()
            .Where(i => i.Id == invite.Id && i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAtUtc, now));
        if (claimed != 1)
        {
            return Invalid();
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = string.IsNullOrWhiteSpace(Input.FullName) ? invite.FullName : Input.FullName.Trim(),
            ShopId = invite.ShopId, // from the invitation only, never from anything the visitor sent
            Role = invite.Role,
            IsActive = true,
        };
        var created = await userManager.CreateAsync(user, Input.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page(); // the transaction is not committed, so the invitation stays valid
        }

        await db.StaffInvites.IgnoreQueryFilters()
            .Where(i => i.Id == invite.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedByUserId, user.Id));
        await tx.CommitAsync();

        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Index");
    }

    private async Task<StaffInvite?> FindPendingAsync(string? token)
    {
        if (!InviteTokens.LooksValid(token))
        {
            return null;
        }

        var hash = InviteTokens.Hash(token!);
        var now = DateTime.UtcNow;

        // The visitor has no shop, so the tenant filter is bypassed here, and only here: by the hash of a secret.
        return await db.StaffInvites.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == hash && i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now);
    }

    private async Task FillAsync(StaffInvite invite)
    {
        Role = invite.Role;
        ShopName = await db.Shops.IgnoreQueryFilters().Where(s => s.Id == invite.ShopId).Select(s => s.Name).FirstOrDefaultAsync() ?? "";
    }

    private IActionResult Invalid()
    {
        Valid = false;
        Response.StatusCode = StatusCodes.Status404NotFound; // the same answer for unknown, expired, used and cancelled
        return Page();
    }

    // The token is in the URL: keep it out of Referer headers, caches and search engines.
    private void Harden()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
