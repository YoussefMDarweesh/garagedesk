using System.Globalization;
using GarageDesk.Web.Models;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Services;

/// <summary>The words shown for a reminder: when it is due, and the WhatsApp message to the customer.</summary>
public static class ReminderText
{
    /// <summary>"3 days overdue", "Due today", "Due at 125,000 km (now 124,800 km)"; date and mileage joined with " · ".</summary>
    public static string DueText(Reminder reminder, DateOnly today, int vehicleMileage, IStringLocalizer<SharedResource> L)
    {
        var parts = new List<string>();

        if (reminder.DueDate is { } date)
        {
            var days = date.DayNumber - today.DayNumber;
            var when = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            parts.Add(days switch
            {
                < -1 => L["{0} days overdue", -days] + $" ({when})",
                -1 => L["1 day overdue"] + $" ({when})",
                0 => L["Due today"],
                1 => L["Due tomorrow"] + $" ({when})",
                _ => L["Due in {0} days", days] + $" ({when})",
            });
        }

        if (reminder.DueMileage is { } mileage)
        {
            parts.Add(L["Due at {0} km (now {1} km)", mileage.ToString("N0", CultureInfo.InvariantCulture),
                vehicleMileage.ToString("N0", CultureInfo.InvariantCulture)]);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The message pre-filled into WhatsApp. It follows the shop's current language.</summary>
    public static string WhatsAppMessage(
        Reminder reminder, string customerName, string? shopName, IStringLocalizer<SharedResource> L)
    {
        var vehicle = reminder.Vehicle;
        var car = $"{vehicle?.Make} {vehicle?.Model}".Trim();

        return L["Hello {0}, this is a reminder from {1}: your {2} (plate {3}) is due for: {4}. Please contact us to book a time. Thank you!",
            customerName, shopName ?? "GarageDesk", car, vehicle?.Plate ?? "", reminder.Type];
    }
}
