namespace GarageDesk.Web.Models;

public enum JobAuditKind
{
    Created = 0,
    StatusChanged = 1,
    PaymentAdded = 2,
    PaymentVoided = 3,
    Assigned = 4,
}

/// <summary>
/// One line of a job card's history: who did what, and when. Append-only: rows are added and never changed or
/// removed (the DbContext refuses it). The person's name is copied in, so history still reads correctly after
/// someone is deactivated or renamed.
/// </summary>
public class JobAuditEntry : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int JobCardId { get; set; }

    public JobCard? JobCard { get; set; }

    public JobAuditKind Kind { get; set; }

    /// <summary>
    /// What happened, in a form the page can translate: Created = the estimate label or empty;
    /// StatusChanged = "Booked>Done"; PaymentAdded / PaymentVoided = "50.00|Cash"; Assigned = the person's name or empty.
    /// </summary>
    public string Detail { get; set; } = "";

    public string? UserId { get; set; }

    public string UserName { get; set; } = "";

    public DateTime AtUtc { get; set; }
}
