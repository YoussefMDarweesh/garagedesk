namespace GarageDesk.Web.Models;

/// <summary>What a share link opens for the customer.</summary>
public enum ShareLinkKind
{
    /// <summary>The invoice of a job card. TargetId is the job card id.</summary>
    Invoice = 0,

    /// <summary>An estimate. TargetId is the estimate id.</summary>
    Estimate = 1,

    /// <summary>The "where is my car" status page of a job card. TargetId is the job card id.</summary>
    Status = 2,
}

/// <summary>
/// A secret public link (/s/{token}) that lets a customer see one invoice, estimate or car status without logging in.
/// The token itself is never stored in the clear: only its SHA-256 hash (used to look the link up) and an
/// encrypted copy (so the owner can copy the same link again later).
/// </summary>
public class ShareLink : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public ShareLinkKind Kind { get; set; }

    /// <summary>The job card or estimate id (see Kind). Always read together with ShopId, never alone.</summary>
    public int TargetId { get; set; }

    /// <summary>SHA-256 of the token (32 bytes). Unique. This is what a public request is matched against.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>The token encrypted with ASP.NET Data Protection, so the owner can show the link again. Null if unavailable.</summary>
    public string? TokenProtected { get; set; }

    /// <summary>The language the customer first sees ("en" or "ar") when they have not picked one.</summary>
    public string Language { get; set; } = "en";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public DateTime? LastViewedAtUtc { get; set; }

    public int ViewCount { get; set; }

    public bool IsLive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
