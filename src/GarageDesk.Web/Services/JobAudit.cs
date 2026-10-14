using System.Globalization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

/// <summary>Writing the job card history. Entries are added in the same save as the change they describe.</summary>
public static class JobAudit
{
    /// <summary>Queues one history entry. Call before SaveChanges so the change and its record commit together.</summary>
    public static void Add(ApplicationDbContext db, StaffContext? actor, JobCard job, JobAuditKind kind, string detail = "")
    {
        db.JobAuditEntries.Add(new JobAuditEntry
        {
            JobCard = job,
            Kind = kind,
            Detail = detail,
            UserId = actor?.UserId,
            UserName = actor?.DisplayName ?? "",
            AtUtc = DateTime.UtcNow,
        });
    }

    public static string StatusDetail(JobStatus from, JobStatus to) => $"{from}>{to}";

    public static string PaymentDetail(decimal amount, PaymentMethod method) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture) + "|" + method;
}
