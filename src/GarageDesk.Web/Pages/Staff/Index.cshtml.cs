using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Staff;

/// <summary>One person on the team, as the page shows them.</summary>
public sealed record MemberRow(string Id, string Name, string Email, ShopRole Role, bool IsActive, bool IsSelf, decimal CommissionPercent);

/// <summary>The invitation form.</summary>
public class InviteInput
{
    [StringLength(100)]
    [Display(Name = "Name")]
    public string? FullName { get; set; }

    [StringLength(30)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Display(Name = "Role")]
    public ShopRole Role { get; set; } = ShopRole.Cashier;
}

/// <summary>The team: members, roles, activation and invitations. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class IndexModel(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    ICurrentShop currentShop,
    ICurrentStaff currentStaff,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InviteInput Invite { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public List<MemberRow> Members { get; private set; } = [];

    public List<StaffInvite> PendingInvites { get; private set; } = [];

    /// <summary>The link of the invitation that was just created. Shown once: only its hash is stored.</summary>
    public string? NewInviteLink { get; private set; }

    public string? NewInviteWhatsApp { get; private set; }

    public string? NewInviteRoleLabel { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostInviteAsync()
    {
        // Anything other than the three roles (a forged number, say) is refused.
        if (!StaffRules.Assignable.Contains(Invite.Role))
        {
            ModelState.AddModelError("Invite.Role", localizer["Choose a role."]);
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(Invite.Phone))
        {
            if (EgyptianPhone.TryNormalize(Invite.Phone, out var normalized))
            {
                phone = normalized;
            }
            else
            {
                ModelState.AddModelError("Invite.Phone", localizer["Enter a valid Egyptian phone number."]);
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var staff = (await currentStaff.GetAsync())!;
        var (token, hash) = InviteTokens.Create();
        var now = DateTime.UtcNow;
        db.StaffInvites.Add(new StaffInvite
        {
            TokenHash = hash,
            Role = Invite.Role,
            FullName = string.IsNullOrWhiteSpace(Invite.FullName) ? null : Invite.FullName.Trim(),
            Phone = phone,
            CreatedByUserId = staff.UserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + StaffInvite.Lifetime,
        });
        await db.SaveChangesAsync();

        // Rendered right here (not redirected), because the token is not stored and cannot be shown again.
        NewInviteLink = Url.PageLink("/Join", values: new { token });
        NewInviteRoleLabel = RoleLabel(Invite.Role);
        var shopName = User.FindFirst(CurrentShop.NameClaimType)?.Value ?? "GarageDesk";
        NewInviteWhatsApp = WhatsAppLink.Build(phone, localizer[
            "Hello {0}, you are invited to join {1} on GarageDesk as {2}. Open this link to create your login: {3}",
            Invite.FullName?.Trim() ?? "", shopName, NewInviteRoleLabel, NewInviteLink ?? ""].Value);

        Invite = new InviteInput();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var invite = await db.StaffInvites.FirstOrDefaultAsync(i => i.Id == id); // filtered to this shop
        if (invite is null)
        {
            return NotFound();
        }

        if (invite.UsedAtUtc is null && invite.RevokedAtUtc is null)
        {
            invite.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        StatusMessage = localizer["Invitation cancelled."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRoleAsync(string id, ShopRole role) => await ChangeAsync(id, role, null);

    public async Task<IActionResult> OnPostActiveAsync(string id, bool active) => await ChangeAsync(id, null, active);

    /// <summary>Changes a member's role and/or active flag, keeping the shop's last owner safe.</summary>
    private async Task<IActionResult> ChangeAsync(string id, ShopRole? newRole, bool? newActive)
    {
        if (currentShop.ShopId is not int shopId)
        {
            return Forbid();
        }

        if (newRole is { } r && !StaffRules.Assignable.Contains(r))
        {
            return BadRequest();
        }

        var me = (await currentStaff.GetAsync())!;

        await using var tx = await db.Database.BeginTransactionAsync();
        await StaffRules.LockTeamAsync(db, shopId);

        // Only people of THIS shop can be found here, whatever id was posted.
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.ShopId == shopId);
        if (target is null)
        {
            return NotFound();
        }

        var role = newRole ?? target.Role;
        var active = newActive ?? target.IsActive;
        if (role == target.Role && active == target.IsActive)
        {
            return RedirectToPage();
        }

        if (target.Id == me.UserId && !active)
        {
            return Fail("You cannot deactivate your own account.");
        }

        var otherActiveOwners = await db.Users.CountAsync(u =>
            u.ShopId == shopId && u.Id != target.Id && u.IsActive && u.Role == ShopRole.Owner);
        if (StaffRules.WouldLeaveNoActiveOwner(target.Role, target.IsActive, role, active, otherActiveOwners))
        {
            return Fail("A shop needs at least one active owner.");
        }

        target.Role = role;
        target.IsActive = active;
        var result = await userManager.UpdateAsync(target);
        if (result.Succeeded)
        {
            // A new stamp makes every login cookie of this person invalid at its next check.
            result = await userManager.UpdateSecurityStampAsync(target);
        }

        if (!result.Succeeded)
        {
            return Fail("That change could not be saved.");
        }

        await tx.CommitAsync();
        StatusMessage = localizer["Team updated."];
        return RedirectToPage();
    }

    private IActionResult Fail(string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var me = (await currentStaff.GetAsync())!;
        var shopId = currentShop.ShopId!.Value;

        // AppUser has no tenant query filter (a login has to find the user before it knows the shop),
        // so the shop is applied explicitly, here and in every staff query.
        var users = await db.Users.AsNoTracking()
            .Where(u => u.ShopId == shopId)
            .OrderBy(u => u.FullName ?? u.Email)
            .ToListAsync();
        Members = users
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.Role).ThenBy(u => u.DisplayLabel)
            .Select(u => new MemberRow(u.Id, u.DisplayLabel, u.Email ?? "", u.Role, u.IsActive, u.Id == me.UserId, u.CommissionPercent))
            .ToList();

        var now = DateTime.UtcNow;
        PendingInvites = (await db.StaffInvites.AsNoTracking()
                .Where(i => i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
                .ToListAsync())
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToList();
    }

    public string RoleLabel(ShopRole role) => role switch
    {
        ShopRole.Owner => localizer["Owner"].Value,
        ShopRole.Cashier => localizer["Cashier"].Value,
        ShopRole.Technician => localizer["Technician"].Value,
        _ => role.ToString(),
    };
}
