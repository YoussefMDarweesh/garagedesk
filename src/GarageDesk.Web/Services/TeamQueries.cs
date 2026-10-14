using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// Reading people of the current shop. AppUser has no tenant query filter (a login must find the user before it
/// knows the shop), so every query for staff goes through here and states the shop explicitly.
/// </summary>
public static class TeamQueries
{
    /// <summary>The roles that can be given a job card.</summary>
    private static readonly ShopRole[] CanWorkOnJobs = [ShopRole.Technician, ShopRole.Owner];

    public static IQueryable<AppUser> MembersOf(this ApplicationDbContext db, int shopId) =>
        db.Users.Where(u => u.ShopId == shopId);

    /// <summary>The active people of this shop who can be assigned a job.</summary>
    public static async Task<List<AppUser>> AssigneesAsync(this ApplicationDbContext db, int shopId) =>
        await db.MembersOf(shopId)
            .AsNoTracking()
            .Where(u => u.IsActive && CanWorkOnJobs.Contains(u.Role))
            .OrderBy(u => u.FullName ?? u.Email)
            .ToListAsync();

    /// <summary>
    /// Can this user id be put on a job card of this shop? It must be a member of the same shop, active, and a
    /// technician or owner. Whatever id was posted, a person of another shop is never accepted.
    /// </summary>
    public static Task<bool> CanBeAssignedAsync(this ApplicationDbContext db, int shopId, string userId) =>
        db.MembersOf(shopId).AnyAsync(u => u.Id == userId && u.IsActive && CanWorkOnJobs.Contains(u.Role));

    public static List<SelectListItem> ToOptions(this IEnumerable<AppUser> users, string? selected) =>
        users.Select(u => new SelectListItem(u.DisplayLabel, u.Id, u.Id == selected)).ToList();
}
