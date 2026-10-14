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

    /// <summary>Quantity times unit price, rounded to the piastre (half away from zero, as on a printed invoice).</summary>
    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
