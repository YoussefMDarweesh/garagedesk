using Microsoft.AspNetCore.Identity;

namespace GarageDesk.Web.Models;

/// <summary>What a person may do inside their shop. A user has exactly one role, in exactly one shop.</summary>
public enum ShopRole
{
    /// <summary>Everything: settings, staff, reports, money. The person who registered the shop is an Owner.</summary>
    Owner = 0,

    /// <summary>Front desk: customers, vehicles, job cards, payments, invoices, estimates, receivables. No settings, staff or reports.</summary>
    Cashier = 1,

    /// <summary>Works on the cars assigned to them. Never sees prices, totals, payments or customer phone numbers.</summary>
    Technician = 2,
}

/// <summary>A login. ShopId is null until the owner has created their shop.</summary>
public class AppUser : IdentityUser
{
    public int? ShopId { get; set; }
    public Shop? Shop { get; set; }

    /// <summary>The user's role in their shop. Everyone who existed before roles were introduced is an Owner.</summary>
    public ShopRole Role { get; set; } = ShopRole.Owner;

    /// <summary>A deactivated user cannot sign in and any session they have stops working. Rows are never deleted, so history keeps its names.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The name shown in lists and on job cards. Optional: the email is used when empty.</summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Commission as a percent of the LABOR total of the Paid job cards assigned to this person (0 to 100).
    /// Only an Owner can change it.
    /// </summary>
    public decimal CommissionPercent { get; set; }

    /// <summary>The full name, or the part of the email before the "@".</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(FullName) ? FullName
        : (Email ?? UserName ?? "").Split('@')[0];
}
