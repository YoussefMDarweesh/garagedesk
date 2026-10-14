using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

public static class ReminderQueries
{
    /// <summary>
    /// The reminders the dashboard shows: not done, and either overdue or coming due (within 14 days, or within
    /// 500 km of the vehicle's mileage). This is a database-side pre-filter; Reminder.StatusFor decides the label.
    /// </summary>
    public static IQueryable<Reminder> DueOrOverdue(this IQueryable<Reminder> reminders, DateOnly today)
    {
        var horizon = today.AddDays(Reminder.DueSoonDays);

        return reminders.Where(r => !r.IsDone
            && ((r.DueDate != null && r.DueDate <= horizon)
                || (r.DueMileage != null && r.DueMileage <= r.Vehicle!.Mileage + Reminder.DueSoonKm)));
    }
}
