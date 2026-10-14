using GarageDesk.Web.Services;

namespace GarageDesk.Web.Models;

/// <summary>Where a job card is in its life. The order is the normal flow: Booked, then In progress, then Done, then Paid.</summary>
public enum JobStatus
{
    Booked = 0,
    InProgress = 1,
    Done = 2,
    Paid = 3,
}

/// <summary>One visit of one vehicle to the shop.</summary>
public class JobCard : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    public DateOnly DateIn { get; set; }

    public int MileageAtVisit { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Booked;

    /// <summary>When the status last changed (UTC). Null if it never changed; the customer's status page then shows the visit date.</summary>
    public DateTime? StatusChangedAtUtc { get; set; }

    /// <summary>Per-shop invoice number (1, 2, ...), given once when the job first reaches Done or Paid. Null until then, never changes.</summary>
    /// <summary>The estimate this job card was created from, if any.</summary>
    public int? EstimateId { get; set; }

    public int? InvoiceNumber { get; set; }

    /// <summary>"INV-0001", or null while the job card has not been issued yet.</summary>
    public string? InvoiceLabel => InvoiceNumber is { } n ? DocumentNumbers.Format(DocumentNumbers.InvoicePrefix, n) : null;

    public string? Notes { get; set; }

    /// <summary>The team member (a technician, or an owner who works on cars) doing this job. Always someone of the same shop.</summary>
    public string? AssignedToUserId { get; set; }

    public AppUser? AssignedTo { get; set; }

    public List<JobLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];

    /// <summary>Optional fixed discount in EGP. Only one of the two discounts is used (a percent wins).</summary>
    public decimal? DiscountAmount { get; set; }

    /// <summary>Optional discount as a percent of the subtotal.</summary>
    public decimal? DiscountPercent { get; set; }

    /// <summary>The shop's VAT percent as it was when this job card was issued; null means no VAT.</summary>
    public decimal? VatPercent { get; set; }

    /// <summary>Sum of all lines, before discount and VAT.</summary>
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    public decimal DiscountValue => JobTotals.Discount(Subtotal, DiscountAmount, DiscountPercent);

    public decimal VatValue => JobTotals.Vat(Subtotal - DiscountValue, VatPercent);

    /// <summary>Sum of the parts lines.</summary>
    public decimal PartsTotal => Lines.Where(l => l.Type == JobLineType.Part).Sum(l => l.LineTotal);

    /// <summary>Sum of the labor lines.</summary>
    public decimal LaborTotal => Lines.Where(l => l.Type == JobLineType.Labor).Sum(l => l.LineTotal);

    /// <summary>What the customer pays: subtotal, minus the discount, plus VAT. Worked out, never stored.</summary>
    public decimal Total => JobTotals.Total(Subtotal, DiscountAmount, DiscountPercent, VatPercent);

    /// <summary>Money received so far.</summary>
    public decimal PaidTotal => Payments.Sum(p => p.Amount);

    /// <summary>What the customer still owes: the total minus the payments. Computed, never stored.</summary>
    public decimal Balance => IsLocked ? 0 : Math.Round(Total - PaidTotal, 2, MidpointRounding.AwayFromZero);

    public PaymentState PaymentState =>
        IsLocked || (Total > 0 && Balance <= 0) ? PaymentState.Paid : PaidTotal > 0 ? PaymentState.Partial : PaymentState.Unpaid;

    /// <summary>A paid job card is final: its lines and details can no longer be changed.</summary>
    public bool IsLocked => Status == JobStatus.Paid;
}
