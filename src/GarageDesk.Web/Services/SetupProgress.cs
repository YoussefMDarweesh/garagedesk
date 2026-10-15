using GarageDesk.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>What the dashboard's setup checklist needs: the progress, and whether the viewer may open shop settings.</summary>
public sealed record SetupChecklistModel(SetupProgress Progress, bool IsOwner);

/// <summary>
/// How far a new shop is with getting set up. It is worked out from the shop's real data every time, never from
/// a stored flag, so it survives a refresh, cannot drift, and ticks itself when something is added anywhere in the app.
/// </summary>
public sealed record SetupProgress(
    bool HasLogo, bool HasAccent, bool HasVat, bool HasCustomer, bool HasVehicle, bool HasJob)
{
    /// <summary>"Make it yours": a logo or a chosen accent colour. (The default orange is never stored, so it cannot be told apart from "not chosen".)</summary>
    public bool HasBranding => HasLogo || HasAccent;

    /// <summary>The three things every shop does: a customer, their car, a job card. The checklist stays until these are done.</summary>
    public bool CoreDone => HasCustomer && HasVehicle && HasJob;

    /// <summary>The first wizard step (1 brand, 2 customer and car, 3 first job card, 4 all done) that still has something to do.</summary>
    public int NextWizardStep =>
        !HasCustomer || !HasVehicle ? (HasCustomer || HasVehicle || HasBranding || HasVat ? 2 : 1) : !HasJob ? 3 : 4;

    /// <summary>How many of the checklist items (see <see cref="Items"/>) are ticked, and how many there are.</summary>
    public (int Done, int Total) Count(bool includeShopSettings)
    {
        var items = Items(includeShopSettings);
        return (items.Count(i => i.Done), items.Count);
    }

    public sealed record Item(string Key, bool Done, bool Optional);

    /// <summary>The checklist, in order. Shop settings items are only for owners (cashiers cannot open that page).</summary>
    public IReadOnlyList<Item> Items(bool includeShopSettings)
    {
        var items = new List<Item>();
        if (includeShopSettings)
        {
            items.Add(new("brand", HasBranding, Optional: true));
            items.Add(new("vat", HasVat, Optional: true));
        }
        items.Add(new("customer", HasCustomer, Optional: false));
        items.Add(new("vehicle", HasVehicle, Optional: false));
        items.Add(new("job", HasJob, Optional: false));
        return items;
    }

    /// <summary>Every query goes through the tenant filter, so this is always the signed-in shop's own progress.</summary>
    public static async Task<SetupProgress> LoadAsync(ApplicationDbContext db)
    {
        var shop = await db.Shops.AsNoTracking()
            .Select(s => new { s.AccentColor, s.VatPercent })
            .FirstOrDefaultAsync();

        return new SetupProgress(
            HasLogo: await db.ShopLogos.AnyAsync(),
            HasAccent: Accents.IsPreset(shop?.AccentColor),
            HasVat: shop?.VatPercent is > 0,
            HasCustomer: await db.Customers.AnyAsync(),
            HasVehicle: await db.Vehicles.AnyAsync(),
            HasJob: await db.JobCards.AnyAsync());
    }
}
