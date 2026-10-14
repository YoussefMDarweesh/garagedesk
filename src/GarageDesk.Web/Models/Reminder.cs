namespace GarageDesk.Web.Models;

public enum ReminderStatus
{
    /// <summary>Not due yet, and not close.</summary>
    Upcoming = 0,

    /// <summary>Due within the next 14 days, or within 500 km of the vehicle's mileage.</summary>
    DueSoon = 1,

    /// <summary>The date has passed, or the vehicle has gone past the due mileage.</summary>
    Overdue = 2,
}

/// <summary>"This car needs an oil change by 1 March or at 125,000 km." Due by date, by mileage, or both.</summary>
public class Reminder : IShopOwned
{
    /// <summary>How many days ahead a reminder counts as "due soon" (and shows on the dashboard).</summary>
    public const int DueSoonDays = 14;

    /// <summary>How many kilometres before the due mileage a reminder counts as "due soon".</summary>
    public const int DueSoonKm = 500;

    /// <summary>The next oil change is suggested this many months after a job is done...</summary>
    public const int SuggestedOilChangeMonths = 6;

    /// <summary>...or this many kilometres after the vehicle's mileage. The shop can change both.</summary>
    public const int SuggestedOilChangeKm = 10_000;

    public int Id { get; set; }

    public int ShopId { get; set; }

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>What is due, in the shop's own words (e.g. "Oil change").</summary>
    public string Type { get; set; } = "";

    public DateOnly? DueDate { get; set; }

    public int? DueMileage { get; set; }

    public bool IsDone { get; set; }

    /// <summary>
    /// Where this reminder stands today. When it has both a date and a mileage, whichever comes first counts.
    /// "Overdue" means strictly past: due today (or exactly at the due mileage) is "due soon".
    /// </summary>
    public ReminderStatus StatusFor(DateOnly today, int vehicleMileage)
    {
        var overdue = (DueDate is { } date && date < today)
                      || (DueMileage is { } mileage && vehicleMileage > mileage);
        if (overdue)
        {
            return ReminderStatus.Overdue;
        }

        var dueSoon = (DueDate is { } soonDate && soonDate <= today.AddDays(DueSoonDays))
                      || (DueMileage is { } soonMileage && vehicleMileage >= soonMileage - DueSoonKm);
        return dueSoon ? ReminderStatus.DueSoon : ReminderStatus.Upcoming;
    }
}
