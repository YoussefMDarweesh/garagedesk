using GarageDesk.Web.Models;

namespace GarageDesk.Web.Pages.Reminders;

/// <summary>What the shared reminder row (_ReminderRow) needs. The reminder comes with its vehicle and owner loaded.</summary>
/// <param name="Reminder">The reminder, with Vehicle and Vehicle.Customer loaded.</param>
/// <param name="Today">Today, as the shop sees it.</param>
/// <param name="ReturnUrl">Where the Done and Reopen buttons come back to (a local URL).</param>
/// <param name="ShopName">Used in the WhatsApp message.</param>
/// <param name="ShowVehicle">False on a vehicle's own page, where the vehicle is obvious.</param>
/// <param name="Compact">A tighter row with icon buttons, for the dashboard.</param>
public sealed record ReminderRowModel(
    Reminder Reminder, DateOnly Today, string ReturnUrl, string? ShopName, bool ShowVehicle = true, bool Compact = false);
