using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>
/// v1.3: technicians. They see only their own jobs and never a price, total, payment or customer phone number.
/// The "no money" checks are made on the rendered HTML of every page a technician can reach.
/// </summary>
public class TechnicianTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private const string Phone = "+201000000077";

    // Everything a technician must never find in a page. The seeded part costs 123.45 and the labor 80,
    // so the subtotal is 203.45, a 10% discount makes it 183.11 (rounded), and a payment of 77.77 was received.
    private static readonly string[] Forbidden =
    [
        "123.45", "80.00", "203.45", "20.34", "20.35", "183.10", "183.11", "77.77", "105.33", "105.34",
        "1000000077", // the phone number, however it is formatted
        "Unit price", "Balance due", "Payments", "Subtotal", "Discount", "Received", "Who owes me", "Revenue",
        "Invoice / Print", "Receivables", "Commission",
        "/Customers", "/Jobs", "/Estimates", "/Reminders", "/ShopSettings", "/Staff", "/Reports", "/Vehicles", "/Receivables",
        "name=\"q\"", // no customer/phone search box
    ];

    private async Task<(string Owner, string Tech, string OtherTech, int Shop)> ShopAsync(string name)
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, name);
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Tina Tech");
        var other = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Omar Other");
        return (owner, tech, other, shop);
    }

    private async Task<int> PricedJobAsync(int shop, string assignedTo, JobStatus status = JobStatus.Booked, string plate = "TECH 100")
    {
        var id = await TestData.SeedJobAsync(factory, shop, status, assignedTo, plate: plate, phone: Phone);
        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.SingleAsync(j => j.Id == id);
        job.DiscountPercent = 10;
        db.Payments.Add(new Payment { JobCardId = id, Amount = 77.77m, Method = PaymentMethod.Cash, PaidOn = new DateOnly(2026, 3, 2) });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AssertNoMoneyAsync(HttpResponseMessage response, string where)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await TestData.BodyAsync(response);
        foreach (var word in Forbidden)
        {
            Assert.False(html.Contains(word, StringComparison.OrdinalIgnoreCase), $"{where} must not contain \"{word}\".");
        }
    }

    // ---------------------------------------------------------------- what they see

    [Fact]
    public async Task My_jobs_lists_only_my_open_jobs()
    {
        var (_, tech, other, shop) = await ShopAsync("My Jobs Shop");
        var mine = await PricedJobAsync(shop, tech, plate: "MINE 1");
        await PricedJobAsync(shop, other, plate: "THEIRS 2");
        await TestData.SeedJobAsync(factory, shop, plate: "NOBODY 3");
        await PricedJobAsync(shop, tech, JobStatus.Paid, plate: "PAID 4");

        var client = factory.CreateClientFor(tech, shop);
        var html = await TestData.BodyAsync(await client.GetAsync("/Work"));

        Assert.Contains("MINE 1", html);
        Assert.DoesNotContain("THEIRS 2", html);
        Assert.DoesNotContain("NOBODY 3", html);
        Assert.DoesNotContain("PAID 4", html); // finished jobs are hidden by default...
        Assert.Contains($"/Work/Job/{mine}", html);

        var all = await TestData.BodyAsync(await client.GetAsync("/Work?show=all"));
        Assert.Contains("PAID 4", all); // ...and shown with "All"
    }

    [Fact]
    public async Task No_page_a_technician_can_reach_shows_a_price_a_total_a_payment_or_a_phone_number()
    {
        var (_, tech, _, shop) = await ShopAsync("No Money Shop");
        var job = await PricedJobAsync(shop, tech, JobStatus.Done);
        var paid = await PricedJobAsync(shop, tech, JobStatus.Paid, plate: "TECH 200");
        var client = factory.CreateClientFor(tech, shop);

        using var db = factory.CreateContextFor(shop);
        var vehicleId = (await db.JobCards.SingleAsync(j => j.Id == job)).VehicleId;

        // Sanity: the pages do show the work itself.
        var jobPage = await TestData.BodyAsync(await client.GetAsync($"/Work/Job/{job}"));
        Assert.Contains("Oil filter", jobPage);
        Assert.Contains("Hidden Customer", jobPage); // the name is fine, the phone number is not

        await AssertNoMoneyAsync(await client.GetAsync("/Work"), "My jobs");
        await AssertNoMoneyAsync(await client.GetAsync("/Work?show=all"), "My jobs (all)");
        await AssertNoMoneyAsync(await client.GetAsync($"/Work/Job/{job}"), "the job page");
        await AssertNoMoneyAsync(await client.GetAsync($"/Work/Job/{paid}"), "a paid job's page");
        await AssertNoMoneyAsync(await client.GetAsync($"/Work/Vehicle/{vehicleId}"), "the vehicle history");

        // Arabic too.
        var arabic = factory.CreateClientFor(tech, shop);
        arabic.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar");
        await AssertNoMoneyAsync(await arabic.GetAsync($"/Work/Job/{job}"), "the job page (Arabic)");
    }

    [Fact]
    public async Task A_paid_job_looks_like_a_finished_one_and_cannot_be_changed()
    {
        var (_, tech, _, shop) = await ShopAsync("Paid Looks Done");
        var paid = await PricedJobAsync(shop, tech, JobStatus.Paid, plate: "CLOSED 1");
        var client = factory.CreateClientFor(tech, shop);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Work/Job/{paid}"));
        Assert.DoesNotContain("status-paid", html);
        Assert.Contains("This job card is closed", html);
        Assert.DoesNotContain("Add a note", html);

        var token = await TestData.TokenAsync(client, $"/Work/Job/{paid}");
        var add = await client.PostAsync($"/Work/Job/{paid}?handler=AddLine", new FormUrlEncodedContent(
        [
            new("NewLine.Description", "Sneaky"), new("NewLine.Quantity", "1"), new("NewLine.Type", "Part"),
            new("__RequestVerificationToken", token),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.DoesNotContain(await db.JobLines.ToListAsync(), l => l.Description == "Sneaky");
    }

    // ---------------------------------------------------------------- what they can do

    [Fact]
    public async Task A_technician_moves_their_job_forward_and_back_but_never_to_paid()
    {
        var (_, tech, _, shop) = await ShopAsync("Status Shop");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, tech);
        var client = factory.CreateClientFor(tech, shop);

        async Task<HttpResponseMessage> MoveAsync(string to)
        {
            var token = await TestData.TokenAsync(client, $"/Work/Job/{job}");
            return await client.PostAsync($"/Work/Job/{job}?handler=Status&to={to}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        }

        async Task<JobStatus> StatusAsync()
        {
            using var db = factory.CreateContextFor(shop);
            return (await db.JobCards.SingleAsync(j => j.Id == job)).Status;
        }

        await MoveAsync("InProgress");
        Assert.Equal(JobStatus.InProgress, await StatusAsync());

        await MoveAsync("Paid"); // skipping a step and Paid: both refused
        Assert.Equal(JobStatus.InProgress, await StatusAsync());

        await MoveAsync("Done");
        Assert.Equal(JobStatus.Done, await StatusAsync());

        using (var db = factory.CreateContextFor(shop))
        {
            // Done issues the invoice number, exactly as on the front desk's page (shared workflow).
            Assert.NotNull((await db.JobCards.SingleAsync(j => j.Id == job)).InvoiceNumber);
        }

        await MoveAsync("Paid"); // Done to Paid is the front desk's step
        Assert.Equal(JobStatus.Done, await StatusAsync());

        await MoveAsync("InProgress"); // one step back, to fix a mistake
        Assert.Equal(JobStatus.InProgress, await StatusAsync());
    }

    [Fact]
    public async Task A_technician_notes_parts_and_labor_without_a_price_and_can_take_back_only_unpriced_notes()
    {
        var (_, tech, _, shop) = await ShopAsync("Notes Shop");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.InProgress, tech);
        var client = factory.CreateClientFor(tech, shop);

        // Even if a price is sent by hand it is ignored.
        var add = await TestData.SubmitFormAsync(client, $"/Work/Job/{job}?handler=AddLine",
            ("NewLine.Type", "Part"), ("NewLine.Description", "Brake pads"), ("NewLine.Quantity", "2"), ("NewLine.UnitPrice", "999"));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        int noteId;
        int pricedId;
        using (var db = factory.CreateContextFor(shop))
        {
            var note = await db.JobLines.SingleAsync(l => l.Description == "Brake pads");
            Assert.Equal(0m, note.UnitPrice);
            Assert.Equal(2m, note.Quantity);
            noteId = note.Id;
            pricedId = (await db.JobLines.FirstAsync(l => l.JobCardId == job && l.UnitPrice > 0)).Id;
        }

        async Task<HttpStatusCode> RemoveAsync(int lineId)
        {
            var token = await TestData.TokenAsync(client, $"/Work/Job/{job}");
            var r = await client.PostAsync($"/Work/Job/{job}?handler=RemoveLine&lineId={lineId}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
            return r.StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotFound, await RemoveAsync(pricedId)); // a priced line belongs to the front desk
        Assert.Equal(HttpStatusCode.Redirect, await RemoveAsync(noteId));

        using var verify = factory.CreateContextFor(shop);
        Assert.Contains(await verify.JobLines.ToListAsync(), l => l.Id == pricedId);
        Assert.DoesNotContain(await verify.JobLines.ToListAsync(), l => l.Id == noteId);
    }

    // ---------------------------------------------------------------- what they cannot do

    [Fact]
    public async Task A_technician_cannot_open_or_change_a_colleagues_job()
    {
        var (_, tech, other, shop) = await ShopAsync("Colleague Shop");
        var theirs = await PricedJobAsync(shop, other, plate: "THEIRS 9");
        var client = factory.CreateClientFor(tech, shop);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Work/Job/{theirs}")).StatusCode);

        // Authorization comes before anti-forgery, so these need no token to prove the point.
        var move = await client.PostAsync($"/Work/Job/{theirs}?handler=Status&to=InProgress", new FormUrlEncodedContent([]));
        Assert.True(move.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);

        var token = await TestData.TokenAsync(client, "/Work");
        var forged = await client.PostAsync($"/Work/Job/{theirs}?handler=Status&to=InProgress",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);

        using var db = factory.CreateContextFor(shop);
        Assert.Equal(JobStatus.Booked, (await db.JobCards.SingleAsync(j => j.Id == theirs)).Status);
    }

    [Fact]
    public async Task A_technician_reaches_a_vehicle_only_through_a_job_assigned_to_them()
    {
        var (_, tech, other, shop) = await ShopAsync("Vehicle Shop");
        var mine = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, tech, plate: "VEH 1");
        var theirs = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, other, plate: "VEH 2");
        var client = factory.CreateClientFor(tech, shop);

        using var db = factory.CreateContextFor(shop);
        var myVehicle = (await db.JobCards.SingleAsync(j => j.Id == mine)).VehicleId;
        var theirVehicle = (await db.JobCards.SingleAsync(j => j.Id == theirs)).VehicleId;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Work/Vehicle/{myVehicle}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Work/Vehicle/{theirVehicle}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Vehicles/Details/{myVehicle}")).StatusCode); // the priced page
    }

    [Fact]
    public async Task A_technician_of_one_shop_finds_nothing_of_another()
    {
        var (_, techA, _, shopA) = await ShopAsync("Shop A");
        var (_, _, _, shopB) = await ShopAsync("Shop B");
        var jobB = await TestData.SeedJobAsync(factory, shopB, JobStatus.Booked, null, plate: "OTHER SHOP");
        var client = factory.CreateClientFor(techA, shopA);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Work/Job/{jobB}")).StatusCode);

        using var db = factory.CreateContextFor(shopB);
        var vehicleB = (await db.JobCards.SingleAsync(j => j.Id == jobB)).VehicleId;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Work/Vehicle/{vehicleB}")).StatusCode);
    }

    [Fact]
    public async Task The_technician_home_page_is_my_jobs_and_has_no_dashboard_numbers()
    {
        var (_, tech, _, shop) = await ShopAsync("Home Shop");
        await PricedJobAsync(shop, tech, JobStatus.Paid);
        var client = factory.CreateClientFor(tech, shop);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Work", response.Headers.Location?.OriginalString);
    }

    // ---------------------------------------------------------------- assignment (front desk side)

    private static async Task<HttpResponseMessage> CreateJobAsync(HttpClient client, int vehicleId, string? assignee) =>
        await TestData.SubmitFormAsync(client, $"/Jobs/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.DateIn", "2026-04-01"), ("Input.MileageAtVisit", "5000"),
            ("Input.AssignedToUserId", assignee ?? ""));

    private async Task<int> VehicleOfAsync(int shop)
    {
        var job = await TestData.SeedJobAsync(factory, shop, plate: $"V{Guid.NewGuid():N}"[..8]);
        using var db = factory.CreateContextFor(shop);
        return (await db.JobCards.SingleAsync(j => j.Id == job)).VehicleId;
    }

    [Fact]
    public async Task A_job_can_be_assigned_to_a_technician_of_the_same_shop()
    {
        var (owner, tech, _, shop) = await ShopAsync("Assign Shop");
        var vehicle = await VehicleOfAsync(shop);
        var client = factory.CreateClientFor(owner, shop);

        var response = await CreateJobAsync(client, vehicle, tech);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.OrderByDescending(j => j.Id).FirstAsync();
        Assert.Equal(tech, job.AssignedToUserId);

        // It shows on the job card and in the list, and in the technician's own list.
        var details = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{job.Id}"));
        Assert.Contains("Tina Tech", details);
        var list = await TestData.BodyAsync(await client.GetAsync("/Jobs"));
        Assert.Contains("Tina Tech", list);
        var mine = await TestData.BodyAsync(await factory.CreateClientFor(tech, shop).GetAsync("/Work"));
        Assert.Contains($"/Work/Job/{job.Id}", mine);
    }

    [Fact]
    public async Task Assigning_to_a_person_of_another_shop_is_rejected_on_the_server()
    {
        var (ownerA, _, _, shopA) = await ShopAsync("Assign A");
        var (_, techB, _, _) = await ShopAsync("Assign B");
        var vehicle = await VehicleOfAsync(shopA);
        var client = factory.CreateClientFor(ownerA, shopA);

        var response = await CreateJobAsync(client, vehicle, techB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the form is shown again with an error
        Assert.Contains("Choose someone from your team.", await TestData.BodyAsync(response));
        using var db = factory.CreateContextFor(shopA);
        Assert.DoesNotContain(await db.JobCards.ToListAsync(), j => j.VehicleId == vehicle && j.AssignedToUserId == techB);

        // Editing an existing job card the same way.
        var job = await TestData.SeedJobAsync(factory, shopA, plate: "EDIT X1");
        var edit = await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{job}",
            ("Input.DateIn", "2026-04-01"), ("Input.MileageAtVisit", "5000"), ("Input.AssignedToUserId", techB));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var verify = factory.CreateContextFor(shopA);
        Assert.Null((await verify.JobCards.SingleAsync(j => j.Id == job)).AssignedToUserId);
    }

    [Fact]
    public async Task Cashiers_and_deactivated_people_cannot_be_given_jobs()
    {
        var (owner, _, _, shop) = await ShopAsync("Who Can Work");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var gone = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, active: false);
        var vehicle = await VehicleOfAsync(shop);
        var client = factory.CreateClientFor(owner, shop);

        Assert.Equal(HttpStatusCode.OK, (await CreateJobAsync(client, vehicle, cashier)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateJobAsync(client, vehicle, gone)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateJobAsync(client, vehicle, "not-a-real-user-id")).StatusCode);

        // The owner can work on cars too.
        Assert.Equal(HttpStatusCode.Redirect, (await CreateJobAsync(client, vehicle, owner)).StatusCode);
    }

    [Fact]
    public async Task The_jobs_list_filters_to_my_jobs_and_unassigned()
    {
        var (owner, tech, _, shop) = await ShopAsync("Filter Shop");
        await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, owner, plate: "OWNERS 1");
        await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, tech, plate: "TECHS 2");
        await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, null, plate: "LOOSE 3");
        var client = factory.CreateClientFor(owner, shop);

        var mine = await TestData.BodyAsync(await client.GetAsync("/Jobs?assigned=me"));
        Assert.Contains("OWNERS 1", mine);
        Assert.DoesNotContain("TECHS 2", mine);
        Assert.DoesNotContain("LOOSE 3", mine);

        var none = await TestData.BodyAsync(await client.GetAsync("/Jobs?assigned=none"));
        Assert.Contains("LOOSE 3", none);
        Assert.DoesNotContain("TECHS 2", none);

        var theirs = await TestData.BodyAsync(await client.GetAsync($"/Jobs?assigned={tech}"));
        Assert.Contains("TECHS 2", theirs);
        Assert.DoesNotContain("OWNERS 1", theirs);
    }
}
