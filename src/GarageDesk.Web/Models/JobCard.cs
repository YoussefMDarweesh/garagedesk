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

    /// <summary>Per-shop invoice number (1, 2, ...), given once when the job first reaches Done or Paid. Null until then, never changes.</summary>
    public int? InvoiceNumber { get; set; }

    /// <summary>"INV-0001", or null while the job card has not been issued yet.</summary>
    public string? InvoiceLabel => InvoiceNumber is { } n ? DocumentNumbers.Format(DocumentNumbers.InvoicePrefix, n) : null;

    public string? Notes { get; set; }

    public List<JobLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];

    /// <summary>Sum of the parts lines.</summary>
    public decimal PartsTotal => Lines.Where(l => l.Type == JobLineType.Part).Sum(l => l.LineTotal);

    /// <summary>Sum of the labor lines.</summary>
    public decimal LaborTotal => Lines.Where(l => l.Type == JobLineType.Labor).Sum(l => l.LineTotal);

    /// <summary>What the customer pays: every line, worked out automatically.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);

    /// <summary>Money received so far.</summary>
    public decimal PaidTotal => Payments.Sum(p => p.Amount);

    /// <summary>What the customer still owes: the total minus the payments. Computed, never stored.</summary>
    public decimal Balance => IsLocked ? 0 : Math.Round(Total - PaidTotal, 2, MidpointRounding.AwayFromZero);

    public PaymentState PaymentState =>
        IsLocked || (Total > 0 && Balance <= 0) ? PaymentState.Paid : PaidTotal > 0 ? PaymentState.Partial : PaymentState.Unpaid;

    /// <summary>A paid job card is final: its lines and details can no longer be changed.</summary>
    public bool IsLocked => Status == JobStatus.Paid;
}
