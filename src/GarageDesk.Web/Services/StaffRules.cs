using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>The rules for changing who is on the team, in one place so they can be tested without a web page.</summary>
public static class StaffRules
{
    /// <summary>The roles an owner may invite or assign. Owner is allowed on purpose: a second owner is how a shop hands over control.</summary>
    public static readonly IReadOnlyList<ShopRole> Assignable = [ShopRole.Owner, ShopRole.Cashier, ShopRole.Technician];

    /// <summary>
    /// A shop must always keep at least one active owner. Given the member's current state and what is about to
    /// happen to them, would that leave the shop with no active owner?
    /// </summary>
    public static bool WouldLeaveNoActiveOwner(
        ShopRole currentRole, bool currentlyActive, ShopRole newRole, bool newActive, int otherActiveOwners)
    {
        var wasActiveOwner = currentRole == ShopRole.Owner && currentlyActive;
        var staysActiveOwner = newRole == ShopRole.Owner && newActive;
        return wasActiveOwner && !staysActiveOwner && otherActiveOwners == 0;
    }

    /// <summary>
    /// Takes a row lock on the shop, so two owners changing the team at the same moment run one after the other and
    /// cannot both pass the "last owner" check. (A no-op UPDATE still locks the row in PostgreSQL.) Call inside a transaction.
    /// </summary>
    public static Task LockTeamAsync(ApplicationDbContext db, int shopId) =>
        db.Shops.IgnoreQueryFilters()
            .Where(s => s.Id == shopId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, x => x.Name));
}
