using GarageDesk.Web.Services;

namespace GarageDesk.Web.Models;

public enum EstimateStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Declined = 3,
}

/// <summary>A quotation for a vehicle. Accepting it creates a job card with the same lines.</summary>
public class Estimate : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    /// <summary>Per-shop number (1, 2, ...) from the same counter mechanism as invoices; shown as EST-0001.</summary>
    public int Number { get; set; }

    public string Label => DocumentNumbers.Format(DocumentNumbers.EstimatePrefix, Number);

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    public DateOnly CreatedOn { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public EstimateStatus Status { get; set; } = EstimateStatus.Draft;

    public string? Notes { get; set; }

    public decimal? DiscountAmount { get; set; }

    public decimal? DiscountPercent { get; set; }

    /// <summary>The shop's VAT percent when the estimate was created; null means no VAT.</summary>
    public decimal? VatPercent { get; set; }

    /// <summary>Set when the estimate was converted; the job card is linked back through JobCard.EstimateId.</summary>
    public int? JobCardId { get; set; }

    public List<EstimateLine> Lines { get; set; } = [];

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    public decimal DiscountValue => JobTotals.Discount(Subtotal, DiscountAmount, DiscountPercent);

    public decimal VatValue => JobTotals.Vat(Subtotal - DiscountValue, VatPercent);

    public decimal Total => JobTotals.Total(Subtotal, DiscountAmount, DiscountPercent, VatPercent);

    public bool IsLocked => Status == EstimateStatus.Accepted || JobCardId is not null;
}

public class EstimateLine : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int EstimateId { get; set; }

    public Estimate? Estimate { get; set; }

    public JobLineType Type { get; set; }

    public string Description { get; set; } = "";

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>The catalog part this quoted line refers to. Quoting never touches stock; converting to a job card does.</summary>
    public int? PartId { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
