using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public partial class RemindersTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static DateOnly Today => ShopClock.Today();

    // ---- "Done when: an overdue oil-change reminder appears on the dashboard and the button opens WhatsApp
    //      with the customer's number and message" ----

    [Fact]
    public async Task An_overdue_reminder_is_on_the_dashboard_with_a_whatsapp_button_for_the_customer()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Reminder Garage");
        await SeedReminderAsync(shopId, "Ahmed Hassan", "+201012345678", "ABC 1234", "Oil change", dueDate: Today.AddDays(-10));
        var client = factory.CreateClientFor(userId, shopId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, "Reminder Garage");

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("Reminders due", html);
        Assert.Contains("Oil change", html);
        Assert.Contains("Overdue", html);
        Assert.Contains("10 days overdue", html);
        Assert.Contains("ABC 1234", html);
        Assert.Contains("Ahmed Hassan", html);

        // The button opens WhatsApp for the customer's number, with the message already typed in.
        var link = WhatsAppHref(html);
        Assert.StartsWith("https://wa.me/201012345678?text=", link);
        var message = Uri.UnescapeDataString(link[(link.IndexOf("?text=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains("Ahmed Hassan", message);
        Assert.Contains("Reminder Garage", message);
        Assert.Contains("Skoda Octavia", message);
        Assert.Contains("ABC 1234", message);
        Assert.Contains("Oil change", message);
        Assert.Matches(@"target=""_blank""[^>]*rel=""noopener noreferrer""", html);
    }

    [Fact]
    public async Task The_dashboard_shows_what_is_due_or_overdue_and_not_the_rest()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Window Garage");
        await SeedReminderAsync(shopId, "Overdue Owner", "+201000000001", "OVD 1", "Overdue job", dueDate: Today.AddDays(-1));
        await SeedReminderAsync(shopId, "Today Owner", "+201000000002", "TOD 1", "Due today job", dueDate: Today);
        await SeedReminderAsync(shopId, "Edge Owner", "+201000000003", "EDG 1", "Day fourteen job", dueDate: Today.AddDays(14));
        await SeedReminderAsync(shopId, "Later Owner", "+201000000004", "LAT 1", "Day fifteen job", dueDate: Today.AddDays(15));
        await SeedReminderAsync(shopId, "Done Owner", "+201000000005", "DON 1", "Already done job", dueDate: Today.AddDays(-5), isDone: true);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("Overdue job", html);
        Assert.Contains("Due today job", html);
        Assert.Contains("Day fourteen job", html);
        Assert.DoesNotContain("Day fifteen job", html);
        Assert.DoesNotContain("Already done job", html);
    }

    [Fact]
    public async Task A_reminder_due_by_mileage_shows_when_the_vehicle_is_close_or_past_it()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Mileage Reminder Garage");
        await SeedReminderAsync(shopId, "Close Owner", "+201000000001", "CLO 1", "Close by km", dueMileage: 125_000, vehicleMileage: 124_800);
        await SeedReminderAsync(shopId, "Past Owner", "+201000000002", "PST 1", "Past by km", dueMileage: 125_000, vehicleMileage: 130_000);
        await SeedReminderAsync(shopId, "Far Owner", "+201000000003", "FAR 1", "Far by km", dueMileage: 125_000, vehicleMileage: 90_000);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("Close by km", html);
        Assert.Contains("Past by km", html);
        Assert.DoesNotContain("Far by km", html);
        Assert.Contains("Due at 125,000 km (now 124,800 km)", html);
    }

    [Fact]
    public async Task Overdue_reminders_come_before_ones_that_are_only_due_soon()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Order Garage");
        await SeedReminderAsync(shopId, "Soon Owner", "+201000000001", "SOO 1", "Soon job", dueDate: Today.AddDays(3));
        await SeedReminderAsync(shopId, "Late Owner", "+201000000002", "LAT 1", "Late job", dueDate: Today.AddDays(-20));
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.True(html.IndexOf("Late job", StringComparison.Ordinal) < html.IndexOf("Soon job", StringComparison.Ordinal));
    }

    // ---- adding and editing ----

    [Fact]
    public async Task A_reminder_is_added_from_a_vehicle_with_a_date_a_mileage_or_both()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Add Reminder Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "ADD 1", "Adder", "+201000000001");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Reminders/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.Type", "Brake inspection"),
            ("Input.DueDate", "2027-03-01"), ("Input.DueMileage", "150000"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = factory.CreateContextFor(shopId);
        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal("Brake inspection", reminder.Type);
        Assert.Equal(new DateOnly(2027, 3, 1), reminder.DueDate);
        Assert.Equal(150_000, reminder.DueMileage);
        Assert.False(reminder.IsDone);
        Assert.Equal(shopId, reminder.ShopId);
    }

    [Fact]
    public async Task A_reminder_needs_a_type_and_a_date_or_a_mileage()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Validate Reminder Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "VAL 1", "Validator", "+201000000001");
        var client = factory.CreateClientFor(userId, shopId);

        var neither = await TestData.SubmitFormAsync(client, $"/Reminders/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.Type", "Oil change"));
        Assert.Equal(HttpStatusCode.OK, neither.StatusCode);
        Assert.Contains("Enter a due date, a due mileage, or both.", await TestData.BodyAsync(neither));

        var noType = await TestData.SubmitFormAsync(client, $"/Reminders/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.Type", ""), ("Input.DueDate", "2027-03-01"));
        Assert.Equal(HttpStatusCode.OK, noType.StatusCode);
        Assert.Contains("The What is due field is required.", await TestData.BodyAsync(noType));

        using var db = factory.CreateContextFor(shopId);
        Assert.Empty(await db.Reminders.ToListAsync());
    }

    [Fact]
    public async Task A_reminder_can_be_edited()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Edit Reminder Garage");
        var reminderId = await SeedReminderAsync(shopId, "Editor", "+201000000001", "EDT 1", "Old type", dueDate: Today.AddDays(30));
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Reminders/Edit/{reminderId}",
            ("Input.Type", "New type"), ("Input.DueDate", ""), ("Input.DueMileage", "99000"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = factory.CreateContextFor(shopId);
        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal("New type", reminder.Type);
        Assert.Null(reminder.DueDate);
        Assert.Equal(99_000, reminder.DueMileage);
    }

    // ---- done and reopen ----

    [Fact]
    public async Task Marking_a_reminder_done_takes_it_off_the_dashboard_and_it_can_be_reopened()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Done Garage");
        var reminderId = await SeedReminderAsync(shopId, "Doer", "+201000000001", "DNE 1", "Finish me", dueDate: Today.AddDays(-2));
        var client = factory.CreateClientFor(userId, shopId);
        Assert.Contains("Finish me", await TestData.BodyAsync(await client.GetAsync("/")));

        var done = await PostAsync(client, $"/Reminders?handler=Done&id={reminderId}&returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal("/", done.Headers.Location!.ToString()); // back to the dashboard
        Assert.DoesNotContain("Finish me", await TestData.BodyAsync(await client.GetAsync("/")));
        Assert.Contains("Finish me", await TestData.BodyAsync(await client.GetAsync("/Reminders?show=done")));

        await PostAsync(client, $"/Reminders?handler=Reopen&id={reminderId}");
        Assert.Contains("Finish me", await TestData.BodyAsync(await client.GetAsync("/")));
    }

    [Fact]
    public async Task The_done_button_will_not_redirect_to_another_site()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Redirect Garage");
        var reminderId = await SeedReminderAsync(shopId, "Redirector", "+201000000001", "RED 1", "Redirect job", dueDate: Today);
        var client = factory.CreateClientFor(userId, shopId);

        var response = await PostAsync(client, $"/Reminders?handler=Done&id={reminderId}&returnUrl={Uri.EscapeDataString("https://evil.example/")}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Reminders", response.Headers.Location!.ToString());
    }

    // ---- "When a job card is marked Done, offer to create the next oil-change reminder" ----

    [Fact]
    public async Task Marking_a_job_done_offers_the_next_oil_change_reminder_once()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Offer Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "OFF 1", "Offered", "+201000000001", mileage: 100_000);
        int jobId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var job = new JobCard { VehicleId = vehicleId, DateIn = Today, MileageAtVisit = 104_000, Status = JobStatus.InProgress };
            db.JobCards.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        // Moving to Done sends the shop back to the job with the offer.
        var token = await TestData.TokenAsync(client, $"/Jobs/Details/{jobId}");
        var moved = await client.PostAsync($"/Jobs/Details/{jobId}?handler=Status&to=Done", new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);

        var page = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Contains("Create the next oil change reminder?", page);

        // The button leads to the create form, pre-filled: 6 months ahead, 10,000 km past the reading.
        var suggestedDate = Today.AddMonths(6).ToString("yyyy-MM-dd");
        Assert.Contains($"vehicleId={vehicleId}", page);
        Assert.Contains("type=Oil%20change", page);
        Assert.Contains($"dueDate={suggestedDate}", page);
        Assert.Contains("dueMileage=114000", page);

        var form = await TestData.BodyAsync(await client.GetAsync(
            $"/Reminders/Create?vehicleId={vehicleId}&type=Oil%20change&dueDate={suggestedDate}&dueMileage=114000"));
        Assert.Contains("value=\"Oil change\"", form);
        Assert.Contains($"value=\"{suggestedDate}\"", form);
        Assert.Contains("value=\"114000\"", form);

        // It is an offer, shown once: the next visit to the page doesn't nag.
        var again = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.DoesNotContain("Create the next oil change reminder?", again);
    }

    [Fact]
    public async Task Other_status_changes_do_not_make_the_offer()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "No Offer Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "NOF 1", "No Offer", "+201000000001");
        int jobId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var job = new JobCard { VehicleId = vehicleId, DateIn = Today, Status = JobStatus.Booked };
            db.JobCards.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        await PostAsync(client, $"/Jobs/Details/{jobId}?handler=Status&to=InProgress", tokenPage: $"/Jobs/Details/{jobId}");

        var page = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.DoesNotContain("Create the next oil change reminder?", page);
    }

    // ---- on the vehicle page ----

    [Fact]
    public async Task A_vehicle_page_lists_that_vehicles_open_reminders_only()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Vehicle Page Garage");
        var first = await SeedReminderAsync(shopId, "First Owner", "+201000000001", "ONE 1", "Reminder of car one", dueDate: Today.AddDays(40));
        await SeedReminderAsync(shopId, "Second Owner", "+201000000002", "TWO 2", "Reminder of car two", dueDate: Today.AddDays(40));
        int vehicleId;
        using (var db = factory.CreateContextFor(shopId))
        {
            vehicleId = (await db.Reminders.SingleAsync(r => r.Id == first)).VehicleId;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

        Assert.Contains("Reminder of car one", html);
        Assert.DoesNotContain("Reminder of car two", html);
        Assert.Contains("/Reminders/Create?vehicleId=" + vehicleId, html);
    }

    // ---- one shop must never reach another shop's reminders (CLAUDE.md rule 2) ----

    [Fact]
    public async Task Shop_B_cannot_see_edit_or_complete_shop_As_reminders()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        var reminderA = await SeedReminderAsync(shopA, "Alice of A", "+201001110001", "AAA 111", "Secret service of A", dueDate: Today.AddDays(-5));
        await SeedReminderAsync(shopB, "Bob of B", "+201002220002", "BBB 222", "Service of B", dueDate: Today.AddDays(-5));
        var clientB = factory.CreateClientFor(userB, shopB);

        // Not on B's dashboard or list...
        foreach (var page in new[] { "/", "/Reminders", "/Reminders?show=all" })
        {
            var html = await TestData.BodyAsync(await clientB.GetAsync(page));
            Assert.Contains("Service of B", html);
            Assert.DoesNotContain("Secret service of A", html);
            Assert.DoesNotContain("201001110001", html); // A's customer's phone is not even in a WhatsApp link
        }

        // ...cannot be opened, completed or reopened by id...
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Reminders/Edit/{reminderA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostAsync(clientB, $"/Reminders?handler=Done&id={reminderA}", tokenPage: "/Reminders")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostAsync(clientB, $"/Reminders?handler=Reopen&id={reminderA}", tokenPage: "/Reminders")).StatusCode);

        // ...and A's reminder is untouched.
        using var checkA = factory.CreateContextFor(shopA);
        var reminder = await checkA.Reminders.SingleAsync();
        Assert.False(reminder.IsDone);
        Assert.Equal("Secret service of A", reminder.Type);
    }

    [Fact]
    public async Task Shop_B_cannot_attach_a_reminder_to_shop_As_vehicle()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A2");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B2");
        var vehicleA = await SeedVehicleAsync(shopA, "AAA 999", "Alice of A2", "+201001110001");
        await SeedVehicleAsync(shopB, "BBB 999", "Bob of B2", "+201002220002");
        var clientB = factory.CreateClientFor(userB, shopB);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Reminders/Create?vehicleId={vehicleA}")).StatusCode);

        var response = await TestData.SubmitFormAsync(clientB, "/Reminders/Create",
            ("Input.VehicleId", vehicleA.ToString()), ("Input.Type", "Planted"), ("Input.DueDate", "2027-01-01"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose the vehicle.", await TestData.BodyAsync(response));

        var form = await TestData.BodyAsync(await clientB.GetAsync("/Reminders/Create"));
        Assert.Contains("BBB 999", form);
        Assert.DoesNotContain("AAA 999", form);

        using var checkA = factory.CreateContextFor(shopA);
        Assert.Empty(await checkA.Reminders.ToListAsync());
        using var checkB = factory.CreateContextFor(shopB);
        Assert.Empty(await checkB.Reminders.ToListAsync());
    }

    // ---- who can open these pages ----

    [Theory]
    [InlineData("/Reminders")]
    [InlineData("/Reminders/Create")]
    public async Task Visitors_who_are_not_signed_in_cannot_open_the_pages(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_a_shop_is_sent_to_create_one()
    {
        var userId = await TestData.CreateUserWithoutShopAsync(factory);
        var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync("/Reminders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/CreateShop", response.Headers.Location!.ToString());
    }

    // ---- Arabic ----

    [Fact]
    public async Task The_dashboard_and_the_whatsapp_message_follow_the_chosen_language()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic Reminder Garage");
        await SeedReminderAsync(shopId, "أحمد حسن", "+201012345678", "أ ب ج 1234", "تغيير زيت", dueDate: Today.AddDays(-3));
        var client = factory.CreateClientFor(userId, shopId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, "ورشة النور");
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("التذكيرات المستحقة", html);
        Assert.Contains("متأخر 3 يومًا", html);
        Assert.Contains("إرسال عبر واتساب", html);

        var link = WhatsAppHref(html);
        Assert.StartsWith("https://wa.me/201012345678?text=", link);
        var message = Uri.UnescapeDataString(link[(link.IndexOf("?text=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains("مرحبًا أحمد حسن", message);
        Assert.Contains("ورشة النور", message);
        Assert.Contains("تغيير زيت", message);
    }

    // ---- helpers ----

    [GeneratedRegex(@"href=""(https://wa\.me/[^""]+)""")]
    private static partial Regex WhatsAppPattern();

    private static string WhatsAppHref(string html)
    {
        var match = WhatsAppPattern().Match(html);
        Assert.True(match.Success, "No WhatsApp link found");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pathAndQuery, string tokenPage = "/Reminders")
    {
        var token = await TestData.TokenAsync(client, tokenPage);
        return await client.PostAsync(pathAndQuery, new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
    }

    private async Task<int> SeedVehicleAsync(int shopId, string plate, string ownerName, string phone, int mileage = 0)
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = ownerName, Phone = phone };
        var vehicle = new Vehicle
        {
            Customer = customer, Plate = plate, PlateKey = PlateNormalizer.Key(plate),
            Make = "Skoda", Model = "Octavia", Year = 2020, Mileage = mileage,
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle.Id;
    }

    private async Task<int> SeedReminderAsync(
        int shopId, string ownerName, string phone, string plate, string type,
        DateOnly? dueDate = null, int? dueMileage = null, int vehicleMileage = 0, bool isDone = false)
    {
        var vehicleId = await SeedVehicleAsync(shopId, plate, ownerName, phone, vehicleMileage);
        using var db = factory.CreateContextFor(shopId);
        var reminder = new Reminder
        {
            VehicleId = vehicleId, Type = type, DueDate = dueDate, DueMileage = dueMileage, IsDone = isDone,
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();
        return reminder.Id;
    }
}
