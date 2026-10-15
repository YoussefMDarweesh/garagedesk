using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Models;

/// <summary>
/// One item of the shop's parts catalog (an oil filter, a set of brake pads). Job card lines can point at it so
/// stock goes down when it is used. Parts are never deleted, only archived, so old job cards keep their link.
/// </summary>
public class Part : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>The shop's own code or the manufacturer's part number. Optional; unique within the shop when given.</summary>
    [StringLength(40)]
    public string? Sku { get; set; }

    /// <summary>"pcs", "litre", "set"...</summary>
    [Required, StringLength(20)]
    public string Unit { get; set; } = "pcs";

    /// <summary>What the shop pays for one unit. Owner only: never shown to cashiers or technicians.</summary>
    public decimal CostPrice { get; set; }

    /// <summary>What the customer is charged for one unit by default.</summary>
    public decimal SellPrice { get; set; }

    /// <summary>
    /// How many are on the shelf. Changed only together with a <see cref="StockMovement"/>, in the same save, so it
    /// always equals the sum of the movements. The concurrency check makes two simultaneous changes fail instead of
    /// overwriting each other.
    /// </summary>
    [ConcurrencyCheck]
    public decimal QuantityOnHand { get; set; }

    /// <summary>The "running low" line: at or below this the part is flagged. Null means no warning.</summary>
    public decimal? LowStockThreshold { get; set; }

    [StringLength(100)]
    public string? Supplier { get; set; }

    /// <summary>False once archived: it can no longer be picked on new lines, but history keeps pointing at it.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Running low: has a threshold and is at or below it.</summary>
    public bool IsLow => IsActive && LowStockThreshold is { } threshold && QuantityOnHand <= threshold;
}

public enum StockMovementKind
{
    /// <summary>The quantity the part was created with.</summary>
    Opening = 0,

    /// <summary>Stock received from a supplier.</summary>
    Purchase = 1,

    /// <summary>Taken for a job card (quantity change is negative).</summary>
    JobUse = 2,

    /// <summary>Given back because the line was taken off the job card (quantity change is positive).</summary>
    JobReturn = 3,

    /// <summary>A manual correction after counting the shelf, or a loss.</summary>
    Adjustment = 4,
}

/// <summary>
/// One change of a part's quantity. Append-only: rows are added and never edited or removed (the DbContext refuses
/// it), so the quantity can always be explained.
/// </summary>
public class StockMovement : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int PartId { get; set; }

    public Part? Part { get; set; }

    public StockMovementKind Kind { get; set; }

    /// <summary>Positive adds stock, negative takes it.</summary>
    public decimal QuantityChange { get; set; }

    /// <summary>The part's quantity right after this movement.</summary>
    public decimal QuantityAfter { get; set; }

    /// <summary>For purchases: what one unit cost this time.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>The job card this movement belongs to (JobUse and JobReturn).</summary>
    public int? JobCardId { get; set; }

    public JobCard? JobCard { get; set; }

    [StringLength(200)]
    public string Note { get; set; } = "";

    public string? UserId { get; set; }

    [StringLength(100)]
    public string UserName { get; set; } = "";

    public DateTime AtUtc { get; set; }
}
