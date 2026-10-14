namespace GarageDesk.Web.Models;

/// <summary>
/// A one-time invitation to join a shop with a given role. The owner sends the link (for example on WhatsApp);
/// the person opens it, chooses their own email and password, and gets an account in the shop.
/// Only a hash of the token is stored, so a leaked database does not leak working invitation links.
/// </summary>
public class StaffInvite : IShopOwned
{
    /// <summary>How long an invitation link works.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public int Id { get; set; }

    public int ShopId { get; set; }

    /// <summary>SHA-256 of the token as 64 lower-case hex characters. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = "";

    public ShopRole Role { get; set; }

    /// <summary>Who the owner meant (optional, pre-fills the sign-up form).</summary>
    public string? FullName { get; set; }

    /// <summary>Optional phone, only used to open a WhatsApp chat with the link ready to send.</summary>
    public string? Phone { get; set; }

    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the invitation was accepted. An invitation can be accepted exactly once.</summary>
    public DateTime? UsedAtUtc { get; set; }

    public string? UsedByUserId { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public bool IsPending(DateTime nowUtc) => UsedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
