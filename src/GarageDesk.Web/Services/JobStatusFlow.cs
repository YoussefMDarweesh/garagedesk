using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

/// <summary>
/// The rules for moving a job card between statuses.
/// Forward one step at a time: Booked, In progress, Done, Paid.
/// One step back is allowed to fix a mistake (e.g. marked Done too early), except out of Paid, which is final.
/// </summary>
public static class JobStatusFlow
{
    public static bool CanMove(JobStatus from, JobStatus to)
    {
        if (from == to)
        {
            return false;
        }

        // Paid is final.
        if (from == JobStatus.Paid)
        {
            return false;
        }

        var step = (int)to - (int)from;
        return step is 1 or -1;
    }

    public static JobStatus? Next(JobStatus status) =>
        CanMove(status, status + 1) && Enum.IsDefined(status + 1) ? status + 1 : null;

    public static JobStatus? Previous(JobStatus status) =>
        status > JobStatus.Booked && CanMove(status, status - 1) ? status - 1 : null;
}
