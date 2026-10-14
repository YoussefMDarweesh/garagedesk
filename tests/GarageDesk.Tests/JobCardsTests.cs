using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class JobCardsTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---- "Done when: I can open a job for a car, add 2 parts + 1 labor line, and see the correct total" ----

    [Fact]
    public async Task A_job_is_opened_from_a_vehicle_and_two_parts_plus_one_labor_give_the_right_total()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Total Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "ABC 1234", mileage: 120_000);
        var client = factory.CreateClientFor(userId, shopId);

        // The vehicle's page offers the button.
        var vehiclePage = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));
        Assert.Contains($"/Jobs/Create?vehicleId={vehicleId}", vehiclePage);

        // The form starts from the vehicle's mileage.
        var form = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Create?vehicleId={vehicleId}"));
        Assert.Contains("ABC 1234", form);
        Assert.Contains("value=\"120000\"", form);

        var created = await TestData.SubmitFormAsync(client, $"/Jobs/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.DateIn", "2026-10-14"),
            ("Input.MileageAtVisit", "120350"), ("Input.Notes", "Strange noise when braking"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var jobId = JobIdFrom(created);

        await AddLineAsync(client, jobId, "Part", "Brake pads", "2", "150.00");   // 300.00
        await AddLineAsync(client, jobId, "Part", "Brake fluid", "1", "85.50");   //  85.50
        await AddLineAsync(client, jobId, "Labor", "Fit brake pads", "1.5", "200"); // 300.00

        var details = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Contains("Brake pads", details);
        Assert.Contains("Brake fluid", details);
        Assert.Contains("Fit brake pads", details);
        Assert.Matches(@"data-job-total[^>]*>.*?685\.50", Squash(details));

        using var db = factory.CreateContextFor(shopId);
        var job = await db.JobCards.Include(j => j.Lines).SingleAsync();
        Assert.Equal(JobStatus.Booked, job.Status);
        Assert.Equal(new DateOnly(2026, 10, 14), job.DateIn);
        Assert.Equal(120350, job.MileageAtVisit);
        Assert.Equal(685.50m, job.Total);
        Assert.Equal(shopId, job.ShopId);
        Assert.All(job.Lines, l => Assert.Equal(shopId, l.ShopId));
    }

    [Fact]
    public async Task Removing_a_line_updates_the_total()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Remove Line Garage");
        var jobId = await SeedJobAsync(shopId, [("Part", "Oil filter", 1, 100m), ("Labor", "Oil change", 1, 50m)]);
        var client = factory.CreateClientFor(userId, shopId);

        int lineId;
        using (var db = factory.CreateContextFor(shopId))
        {
            lineId = (await db.JobLines.SingleAsync(l => l.Description == "Oil filter")).Id;
        }

        var response = await PostAsync(client, jobId, $"handler=RemoveLine&lineId={lineId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var details = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.DoesNotContain("Oil filter", details);
        Assert.Matches(@"data-job-total[^>]*>.*?50\.00", Squash(details));
    }

    [Fact]
    public async Task A_line_needs_a_description_a_positive_quantity_and_a_price_that_is_not_negative()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Validation Garage");
        var jobId = await SeedJobAsync(shopId);
        var client = factory.CreateClientFor(userId, shopId);

        var response = await AddLineAsync(client, jobId, "Part", "", "0", "-5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await TestData.BodyAsync(response);
        Assert.Contains("The Description field is required.", html);
        Assert.Contains("must be between 0.01 and", html);
        Assert.Contains("must be between 0 and", html);
        using var db = factory.CreateContextFor(shopId);
        Assert.Empty(await db.JobLines.ToListAsync());
    }

    // ---- editing ----

    [Fact]
    public async Task The_visit_details_can_be_edited_until_the_job_is_paid()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Edit Job Garage");
        var jobId = await SeedJobAsync(shopId);
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{jobId}",
            ("Input.DateIn", "2026-09-30"), ("Input.MileageAtVisit", "88000"), ("Input.Notes", "Updated"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = factory.CreateContextFor(shopId);
        var job = await db.JobCards.SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 30), job.DateIn);
        Assert.Equal(88000, job.MileageAtVisit);
        Assert.Equal("Updated", job.Notes);
    }

    // ---- status flow ----

    [Fact]
    public async Task A_job_moves_through_booked_in_progress_done_and_paid()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Flow Garage");
        var jobId = await SeedJobAsync(shopId, [("Part", "Filter", 1, 10m)]);
        var client = factory.CreateClientFor(userId, shopId);

        foreach (var (to, expected) in new[]
                 {
                     ("InProgress", JobStatus.InProgress),
                     ("Done", JobStatus.Done),
                     ("Paid", JobStatus.Paid),
                 })
        {
            var response = await PostAsync(client, jobId, $"handler=Status&to={to}");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(expected, await StatusOfAsync(shopId, jobId));
        }
    }

    [Fact]
    public async Task A_status_cannot_be_skipped()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Skip Garage");
        var jobId = await SeedJobAsync(shopId);
        var client = factory.CreateClientFor(userId, shopId);

        var response = await PostAsync(client, jobId, "handler=Status&to=Paid");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(JobStatus.Booked, await StatusOfAsync(shopId, jobId));
        var page = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Contains("That status change is not allowed.", page);
    }

    [Fact]
    public async Task A_status_can_move_back_one_step_to_fix_a_mistake()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Back Garage");
        var jobId = await SeedJobAsync(shopId, status: JobStatus.Done);
        var client = factory.CreateClientFor(userId, shopId);

        await PostAsync(client, jobId, "handler=Status&to=InProgress");

        Assert.Equal(JobStatus.InProgress, await StatusOfAsync(shopId, jobId));
    }

    [Fact]
    public async Task A_paid_job_is_locked()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Locked Garage");
        var jobId = await SeedJobAsync(shopId, [("Part", "Filter", 1, 10m)], status: JobStatus.Paid);
        var client = factory.CreateClientFor(userId, shopId);

        // No new lines
        await AddLineAsync(client, jobId, "Part", "Sneaky", "1", "1");
        // No removing lines
        int lineId;
        using (var db = factory.CreateContextFor(shopId))
        {
            lineId = (await db.JobLines.SingleAsync()).Id;
        }
        await PostAsync(client, jobId, $"handler=RemoveLine&lineId={lineId}");
        // No going back
        await PostAsync(client, jobId, "handler=Status&to=Done");
        // No editing the visit
        var edit = await client.GetAsync($"/Jobs/Edit/{jobId}");

        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        Assert.StartsWith($"/Jobs/Details/{jobId}", edit.Headers.Location!.ToString());
        using var check = factory.CreateContextFor(shopId);
        Assert.Equal(JobStatus.Paid, (await check.JobCards.SingleAsync()).Status);
        var line = await check.JobLines.SingleAsync();
        Assert.Equal("Filter", line.Description);
    }

    // ---- the list ----

    [Fact]
    public async Task The_list_shows_totals_and_can_be_filtered_by_status_and_searched()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "List Garage");
        var firstId = await SeedJobAsync(shopId, [("Part", "Pads", 2, 100m)], plate: "AAA 111");
        await SeedJobAsync(shopId, [("Labor", "Service", 1, 400m)], plate: "BBB 222", status: JobStatus.Done);
        var client = factory.CreateClientFor(userId, shopId);

        var all = await TestData.BodyAsync(await client.GetAsync("/Jobs"));
        Assert.Contains("AAA 111", all);
        Assert.Contains("BBB 222", all);
        Assert.Contains("200.00", all);
        Assert.Contains("400.00", all);

        var done = await TestData.BodyAsync(await client.GetAsync("/Jobs?status=Done"));
        Assert.Contains("BBB 222", done);
        Assert.DoesNotContain("AAA 111", done);

        var byPlate = await TestData.BodyAsync(await client.GetAsync("/Jobs?q=aaa111"));
        Assert.Contains("AAA 111", byPlate);
        Assert.DoesNotContain("BBB 222", byPlate);

        var byNumber = await TestData.BodyAsync(await client.GetAsync($"/Jobs?q=%23{firstId}"));
        Assert.Contains("AAA 111", byNumber);
        Assert.DoesNotContain("BBB 222", byNumber);

        var none = await TestData.BodyAsync(await client.GetAsync("/Jobs?q=zzzzzz"));
        Assert.Contains("No job cards match your search.", none);
    }

    [Fact]
    public async Task The_home_page_counts_the_open_job_cards()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Open Count Garage");
        await SeedJobAsync(shopId);
        await SeedJobAsync(shopId, status: JobStatus.InProgress);
        await SeedJobAsync(shopId, status: JobStatus.Paid);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        // Booked + In progress are open; Paid is not.
        Assert.Matches(@"Open job cards</span>.*?stat-value"">\s*2\s*</p>", Squash(html));
    }

    // ---- one shop must never reach another shop's job cards (CLAUDE.md rule 2) ----

    [Fact]
    public async Task Shop_B_cannot_see_open_edit_or_change_shop_As_job_cards()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        var jobA = await SeedJobAsync(shopA, [("Part", "Secret part", 1, 999m)], plate: "SECRET 1");
        await SeedJobAsync(shopB, plate: "BEE 222");
        int lineA;
        using (var db = factory.CreateContextFor(shopA))
        {
            lineA = (await db.JobLines.SingleAsync()).Id;
        }
        var clientB = factory.CreateClientFor(userB, shopB);

        // Opening or editing by id is "not found".
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Jobs/Details/{jobA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Jobs/Edit/{jobA}")).StatusCode);

        // Changing it by id is "not found" too, and nothing changes.
        var ownToken = await TestData.TokenAsync(clientB, "/Jobs/Create");
        async Task<HttpResponseMessage> Post(string path) => await clientB.PostAsync(path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["NewLine.Type"] = "Part", ["NewLine.Description"] = "Planted", ["NewLine.Quantity"] = "1",
                ["NewLine.UnitPrice"] = "1", ["Input.DateIn"] = "2026-10-01", ["Input.MileageAtVisit"] = "1",
                ["__RequestVerificationToken"] = ownToken,
            }));
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"/Jobs/Details/{jobA}?handler=AddLine")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"/Jobs/Details/{jobA}?handler=RemoveLine&lineId={lineA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"/Jobs/Details/{jobA}?handler=Status&to=InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"/Jobs/Edit/{jobA}")).StatusCode);

        // B's own job cannot be used to reach A's line either.
        int jobB;
        using (var db = factory.CreateContextFor(shopB))
        {
            jobB = (await db.JobCards.SingleAsync()).Id;
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"/Jobs/Details/{jobB}?handler=RemoveLine&lineId={lineA}")).StatusCode);

        // B's list and searches never include A's job cards.
        var list = await TestData.BodyAsync(await clientB.GetAsync("/Jobs"));
        Assert.Contains("BEE 222", list);
        Assert.DoesNotContain("SECRET 1", list);
        foreach (var q in new[] { "SECRET 1", "SECRET1", $"#{jobA}" })
        {
            var body = await TestData.BodyAsync(await clientB.GetAsync($"/Jobs?q={Uri.EscapeDataString(q)}"));
            Assert.True(body.Contains("No job cards match your search."),
                $"q={q}; rows: " + string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(body, "<tr>.*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline).Select(m => System.Text.RegularExpressions.Regex.Replace(m.Value, @"\s+|<[^>]+>", " ").Trim())));
        }

        // A's job card is exactly as it was.
        using var checkA = factory.CreateContextFor(shopA);
        var job = await checkA.JobCards.Include(j => j.Lines).SingleAsync();
        Assert.Equal(JobStatus.Booked, job.Status);
        Assert.Equal("Secret part", job.Lines.Single().Description);
    }

    [Fact]
    public async Task Shop_B_cannot_open_a_job_card_for_shop_As_vehicle()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A2");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B2");
        var vehicleA = await SeedVehicleAsync(shopA, "AAA 999");
        await SeedVehicleAsync(shopB, "BBB 999");
        var clientB = factory.CreateClientFor(userB, shopB);

        // The prefilled form for A's vehicle does not exist for B...
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Jobs/Create?vehicleId={vehicleA}")).StatusCode);

        // ...a forged post naming A's vehicle is refused...
        var response = await TestData.SubmitFormAsync(clientB, "/Jobs/Create",
            ("Input.VehicleId", vehicleA.ToString()), ("Input.DateIn", "2026-10-14"), ("Input.MileageAtVisit", "1"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose the vehicle.", await TestData.BodyAsync(response));

        // ...and A's vehicle is not even offered in B's list.
        var form = await TestData.BodyAsync(await clientB.GetAsync("/Jobs/Create"));
        Assert.Contains("BBB 999", form);
        Assert.DoesNotContain("AAA 999", form);

        using var checkA = factory.CreateContextFor(shopA);
        Assert.Empty(await checkA.JobCards.ToListAsync());
        using var checkB = factory.CreateContextFor(shopB);
        Assert.Empty(await checkB.JobCards.ToListAsync());
    }

    // ---- who can open these pages ----

    [Theory]
    [InlineData("/Jobs")]
    [InlineData("/Jobs/Create")]
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

        var response = await client.GetAsync("/Jobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/CreateShop", response.Headers.Location!.ToString());
    }

    // ---- Arabic ----

    [Fact]
    public async Task The_job_card_page_is_translated_and_accepts_decimal_quantities_in_arabic()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic Job Garage");
        var jobId = await SeedJobAsync(shopId);
        var client = factory.CreateClientFor(userId, shopId);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var added = await AddLineAsync(client, jobId, "Labor", "تغيير زيت", "1.5", "120.50");
        var addedBody = await TestData.BodyAsync(added);
        Assert.True(added.StatusCode == HttpStatusCode.Redirect,
            "validation errors: " + string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(addedBody, "field-validation-error[^>]*>([^<]*)<").Select(m => m.Groups[1].Value)));

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("أمر شغل", html);
        Assert.Contains("تغيير زيت", html);
        Assert.Contains("ج.م", html);
        Assert.Matches(@"data-job-total[^>]*>.*?180\.75", Squash(html)); // 1.5 x 120.50
    }

    // ---- helpers ----

    private static string Squash(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");

    private static int JobIdFrom(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.ToString();
        return int.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    /// <summary>Posts one of the details page's forms, fetching its anti-forgery token first.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, int jobId, string query)
    {
        var token = await TestData.TokenAsync(client, $"/Jobs/Details/{jobId}");
        return await client.PostAsync($"/Jobs/Details/{jobId}?{query}", new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
    }

    private static async Task<HttpResponseMessage> AddLineAsync(
        HttpClient client, int jobId, string type, string description, string quantity, string unitPrice)
    {
        var token = await TestData.TokenAsync(client, $"/Jobs/Details/{jobId}");
        return await client.PostAsync($"/Jobs/Details/{jobId}?handler=AddLine", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NewLine.Type"] = type,
            ["NewLine.Description"] = description,
            ["NewLine.Quantity"] = quantity,
            ["NewLine.UnitPrice"] = unitPrice,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private async Task<JobStatus> StatusOfAsync(int shopId, int jobId)
    {
        using var db = factory.CreateContextFor(shopId);
        return (await db.JobCards.SingleAsync(j => j.Id == jobId)).Status;
    }

    private async Task<int> SeedVehicleAsync(int shopId, string plate, int mileage = 50_000)
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = $"Owner of {plate}", Phone = "+201000000000" };
        var vehicle = new Vehicle
        {
            Customer = customer, Plate = plate, PlateKey = GarageDesk.Web.Services.PlateNormalizer.Key(plate),
            Make = "Skoda", Model = "Octavia", Year = 2020, Mileage = mileage,
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle.Id;
    }

    private async Task<int> SeedJobAsync(
        int shopId,
        (string Type, string Description, decimal Quantity, decimal UnitPrice)[]? lines = null,
        string? plate = null,
        JobStatus status = JobStatus.Booked)
    {
        var vehicleId = await SeedVehicleAsync(shopId, plate ?? $"SEED {Guid.NewGuid():N}"[..12]);
        using var db = factory.CreateContextFor(shopId);
        var job = new JobCard { VehicleId = vehicleId, DateIn = new DateOnly(2026, 10, 1), MileageAtVisit = 1000, Status = status };
        foreach (var line in lines ?? [])
        {
            job.Lines.Add(new JobLine
            {
                Type = Enum.Parse<JobLineType>(line.Type), Description = line.Description,
                Quantity = line.Quantity, UnitPrice = line.UnitPrice,
            });
        }
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }
}
