namespace GarageDesk.Web.Models;

public enum JobLineType
{
    Labor = 0,
    Part = 1,
}

/// <summary>
/// One line on a job card: a part or some labor. It carries its own ShopId (like every shop-owned row),
/// so the tenant filter protects lines directly and not only through their job card.
/// </summary>
public class JobLine : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int JobCardId { get; set; }

    public JobCard? JobCard { get; set; }

    public JobLineType Type { get; set; }

    public string Description { get; set; } = "";

    /// <summary>How many parts, or how many hours of labor. Can be fractional (1.5 hours).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Price of one unit, in EGP.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The catalog part this line uses (stock was taken for it), or null for free text and labor.</summary>
    public int? PartId { get; set; }

    /// <summary>
    /// What one unit of the part cost the shop when the line was added (a snapshot, so a later price change never
    /// rewrites old profit). Owner only. Null for free text and labor.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Who added the line, so a technician can take back their own catalog part. Not shown anywhere.</summary>
    public string? AddedByUserId { get; set; }

    /// <summary>Quantity times unit price, rounded to the piastre (half away from zero, as on a printed invoice).</summary>
    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
