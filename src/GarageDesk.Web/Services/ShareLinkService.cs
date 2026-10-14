using System.Globalization;
using System.Security.Cryptography;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GarageDesk.Web.Services;

/// <summary>One live link as the owner sees it in the link manager.</summary>
public sealed record ShareLinkRow(
    int Id, ShareLinkKind Kind, string? Url, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, int ViewCount, DateTime? LastViewedAtUtc);

/// <summary>The outcome of "Send on WhatsApp": the public link, and the wa.me link (null if the customer has no usable phone).</summary>
public sealed record WhatsAppShare(string PublicUrl, string? WhatsAppUrl, string Message);

/// <summary>
/// The owner's side of share links: create or reuse a link, list and revoke them, build the WhatsApp message.
/// Everything here goes through the DbContext's tenant filter, so an owner can only ever touch their own shop's
/// jobs, estimates and links. (The customer's side is PublicShareLoader.)
/// </summary>
public class ShareLinkService(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ShareLinkOptions> options,
    IStringLocalizer<SharedResource> localizer)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("GarageDesk.ShareLinks.v1");

    /// <summary>
    /// Opens the link for a target, creating it if there is no live one. Idempotent: pressing the button twice gives
    /// the same link. Null when the target does not exist in the current shop.
    /// </summary>
    public async Task<(ShareLink Link, string Url)?> GetOrCreateAsync(ShareLinkKind kind, int targetId)
    {
        if (!await TargetExistsAsync(kind, targetId))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var candidates = await db.ShareLinks
            .Where(l => l.Kind == kind && l.TargetId == targetId && l.RevokedAtUtc == null && l.ExpiresAtUtc > now)
            .OrderByDescending(l => l.Id)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            if (TryReveal(candidate) is { } existingToken)
            {
                return (candidate, PublicUrl(existingToken));
            }
        }

        var token = ShareTokens.Generate();
        var link = new ShareLink
        {
            Kind = kind,
            TargetId = targetId,
            TokenHash = ShareTokens.Hash(token),
            TokenProtected = _protector.Protect(token),
            Language = CurrentLanguage(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(Math.Max(1, options.Value.ExpiryDays)),
        };
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        return (link, PublicUrl(token));
    }

    /// <summary>The live (not revoked, not expired) links of a target, newest first.</summary>
    public async Task<List<ShareLinkRow>> ListLiveAsync(int targetId, params ShareLinkKind[] kinds)
    {
        var now = DateTime.UtcNow;
        var links = await db.ShareLinks.AsNoTracking()
            .Where(l => l.TargetId == targetId && kinds.Contains(l.Kind) && l.RevokedAtUtc == null && l.ExpiresAtUtc > now)
            .OrderByDescending(l => l.Id)
            .ToListAsync();

        return links
            .Select(l => new ShareLinkRow(l.Id, l.Kind, TryReveal(l) is { } t ? PublicUrl(t) : null,
                l.CreatedAtUtc, l.ExpiresAtUtc, l.ViewCount, l.LastViewedAtUtc))
            .ToList();
    }

    /// <summary>Revokes one link of this target. It stops working immediately. False if there is no such live link in this shop.</summary>
    public async Task<bool> RevokeAsync(int linkId, int targetId, params ShareLinkKind[] kinds)
    {
        var link = await db.ShareLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.TargetId == targetId && kinds.Contains(l.Kind));
        if (link is null)
        {
            return false;
        }

        link.RevokedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Creates or reuses the link and builds the wa.me link with the shop's message. Null if the target is not in this shop.</summary>
    public async Task<WhatsAppShare?> PrepareWhatsAppAsync(ShareLinkKind kind, int targetId)
    {
        var context = await LoadContextAsync(kind, targetId);
        if (context is null)
        {
            return null;
        }

        var opened = await GetOrCreateAsync(kind, targetId);
        if (opened is null)
        {
            return null;
        }

        var language = CurrentLanguage();
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync();
        var custom = language == "ar" ? shop?.ShareMessageAr : shop?.ShareMessageEn;
        var template = !string.IsNullOrWhiteSpace(custom) ? custom : DefaultTemplate(kind, context.JobStatus);

        var message = ShareMessage.Render(template, context.CustomerName, context.Plate, shop?.Name ?? "GarageDesk", opened.Value.Url);
        return new WhatsAppShare(opened.Value.Url, WhatsAppLink.Build(context.Phone, message), message);
    }

    private string DefaultTemplate(ShareLinkKind kind, JobStatus? jobStatus) => kind switch
    {
        ShareLinkKind.Invoice => localizer["Hi {name}, here is your invoice from {shop} for your car {plate}: {link}"].Value,
        ShareLinkKind.Estimate => localizer["Hi {name}, here is your estimate from {shop} for your car {plate}: {link}"].Value,
        _ when jobStatus is JobStatus.Done or JobStatus.Paid => localizer["Hi {name}, your car {plate} is ready. Details: {link}"].Value,
        _ => localizer["Hi {name}, you can follow the status of your car {plate} here: {link}"].Value,
    };

    private sealed record Context(string CustomerName, string? Phone, string Plate, JobStatus? JobStatus);

    private async Task<Context?> LoadContextAsync(ShareLinkKind kind, int targetId)
    {
        if (kind == ShareLinkKind.Estimate)
        {
            return await db.Estimates.AsNoTracking()
                .Where(e => e.Id == targetId)
                .Select(e => new Context(
                    e.Vehicle!.Customer!.Name, e.Vehicle.Customer.Phone, e.Vehicle.Plate, null))
                .FirstOrDefaultAsync();
        }

        return await db.JobCards.AsNoTracking()
            .Where(j => j.Id == targetId)
            .Select(j => new Context(
                j.Vehicle!.Customer!.Name, j.Vehicle.Customer.Phone, j.Vehicle.Plate, j.Status))
            .FirstOrDefaultAsync();
    }

    private async Task<bool> TargetExistsAsync(ShareLinkKind kind, int targetId) =>
        kind == ShareLinkKind.Estimate
            ? await db.Estimates.AnyAsync(e => e.Id == targetId)
            : await db.JobCards.AnyAsync(j => j.Id == targetId);

    private string? TryReveal(ShareLink link)
    {
        if (string.IsNullOrEmpty(link.TokenProtected))
        {
            return null;
        }

        try
        {
            var token = _protector.Unprotect(link.TokenProtected);
            // Belt and braces: the decrypted token must be the one this row's hash belongs to.
            return ShareTokens.IsWellFormed(token) && CryptographicOperations.FixedTimeEquals(ShareTokens.Hash(token), link.TokenHash)
                ? token
                : null;
        }
        catch (CryptographicException)
        {
            // The encryption keys changed (a new container, say). The link still works for the customer;
            // the owner just cannot re-display it and a new one is created next time.
            return null;
        }
    }

    private string PublicUrl(string token)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        return request is null
            ? $"/s/{token}"
            : $"{request.Scheme}://{request.Host}{request.PathBase}/s/{token}";
    }

    private static string CurrentLanguage()
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Cultures.Supported.Contains(lang) ? lang : Cultures.Default;
    }
}
