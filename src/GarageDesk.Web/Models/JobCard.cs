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

    public string? Notes { get; set; }

    public List<JobLine> Lines { get; set; } = [];

    /// <summary>Sum of the parts lines.</summary>
    public decimal PartsTotal => Lines.Where(l => l.Type == JobLineType.Part).Sum(l => l.LineTotal);

    /// <summary>Sum of the labor lines.</summary>
    public decimal LaborTotal => Lines.Where(l => l.Type == JobLineType.Labor).Sum(l => l.LineTotal);

    /// <summary>What the customer pays: every line, worked out automatically.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);

    /// <summary>A paid job card is final: its lines and details can no longer be changed.</summary>
    public bool IsLocked => Status == JobStatus.Paid;
}
