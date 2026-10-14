namespace GarageDesk.Web.Models;

/// <summary>A per-shop counter for one kind of document ("Invoice", later "Estimate"). One row per shop and kind.</summary>
public class NumberSequence : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public string Kind { get; set; } = "";

    /// <summary>The last number handed out. Starts at 0, so the first number is 1.</summary>
    public int LastValue { get; set; }
}
