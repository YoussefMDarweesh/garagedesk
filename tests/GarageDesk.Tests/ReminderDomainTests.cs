using GarageDesk.Web.Models;
using GarageDesk.Web.Services;

namespace GarageDesk.Tests;

public class ReminderDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static Reminder Due(DateOnly? date = null, int? mileage = null) =>
        new() { Type = "Oil change", DueDate = date, DueMileage = mileage };

    // ---- by date ----

    [Theory]
    [InlineData(-30, ReminderStatus.Overdue)]
    [InlineData(-1, ReminderStatus.Overdue)]    // yesterday has passed
    [InlineData(0, ReminderStatus.DueSoon)]     // today is due, not yet overdue
    [InlineData(1, ReminderStatus.DueSoon)]
    [InlineData(14, ReminderStatus.DueSoon)]    // the 14th day still counts
    [InlineData(15, ReminderStatus.Upcoming)]   // the 15th does not
    [InlineData(200, ReminderStatus.Upcoming)]
    public void A_date_makes_a_reminder_overdue_due_soon_or_upcoming(int daysFromToday, ReminderStatus expected)
    {
        var reminder = Due(date: Today.AddDays(daysFromToday));

        Assert.Equal(expected, reminder.StatusFor(Today, vehicleMileage: 50_000));
    }

    // ---- by mileage ----

    [Theory]
    [InlineData(126_000, ReminderStatus.Overdue)]    // gone past 125,000
    [InlineData(125_001, ReminderStatus.Overdue)]
    [InlineData(125_000, ReminderStatus.DueSoon)]    // exactly at the due mileage is due, not overdue
    [InlineData(124_500, ReminderStatus.DueSoon)]    // within 500 km
    [InlineData(124_499, ReminderStatus.Upcoming)]
    [InlineData(100_000, ReminderStatus.Upcoming)]
    public void A_mileage_makes_a_reminder_overdue_due_soon_or_upcoming(int vehicleMileage, ReminderStatus expected)
    {
        var reminder = Due(mileage: 125_000);

        Assert.Equal(expected, reminder.StatusFor(Today, vehicleMileage));
    }

    // ---- both: whichever comes first counts ----

    [Fact]
    public void With_both_the_more_urgent_one_wins()
    {
        // Date is far away, but the car has already driven past the mileage.
        var byMileage = Due(date: Today.AddDays(180), mileage: 100_000);
        Assert.Equal(ReminderStatus.Overdue, byMileage.StatusFor(Today, vehicleMileage: 101_000));

        // Mileage is far away, but the date has passed.
        var byDate = Due(date: Today.AddDays(-3), mileage: 200_000);
        Assert.Equal(ReminderStatus.Overdue, byDate.StatusFor(Today, vehicleMileage: 100_000));

        // Neither is close.
        var neither = Due(date: Today.AddDays(90), mileage: 200_000);
        Assert.Equal(ReminderStatus.Upcoming, neither.StatusFor(Today, vehicleMileage: 100_000));
    }

    // ---- WhatsApp links ----

    [Theory]
    [InlineData("+201012345678", "201012345678")]
    [InlineData("201012345678", "201012345678")]
    [InlineData("+20 101 234 5678", "201012345678")]
    public void The_whatsapp_link_uses_the_number_in_international_digits(string phone, string digits)
    {
        var link = WhatsAppLink.Build(phone, "Hello");

        Assert.Equal($"https://wa.me/{digits}?text=Hello", link);
    }

    [Fact]
    public void The_message_is_url_encoded_and_survives_the_round_trip()
    {
        const string message = "Hello Ahmed & Co, your car (plate ABC 123) is due!\nPlease call #1? 100% — مرحبًا";

        var link = WhatsAppLink.Build("+201012345678", message)!;

        // Nothing that could break the URL is left raw.
        var query = link[(link.IndexOf("?text=", StringComparison.Ordinal) + 6)..];
        Assert.DoesNotContain(" ", query);
        Assert.DoesNotContain("&", query);
        Assert.DoesNotContain("#", query);
        Assert.DoesNotContain("?", query);
        Assert.DoesNotContain("\n", query);
        Assert.Contains("%0A", query);

        // And decoding it gives back exactly the message.
        Assert.Equal(message, Uri.UnescapeDataString(query));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("+20")]
    public void A_phone_that_is_not_a_usable_number_gives_no_link(string? phone)
    {
        Assert.Null(WhatsAppLink.Build(phone, "Hello"));
    }
}
