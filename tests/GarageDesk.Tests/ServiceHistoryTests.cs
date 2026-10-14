using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class ServiceHistoryTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---- the mileage rule, on its own ----

    [Theory]
    [InlineData(100_000, 120_000, 120_000, true)]    // higher reading: the vehicle's mileage goes up
    [InlineData(100_000, 100_000, 100_000, false)]   // same reading: nothing to do
    [InlineData(100_000, 90_000, 100_000, false)]    // lower reading: never wound back
    [InlineData(0, 1, 1, true)]
    [InlineData(100_000, 0, 100_000, false)]
    public void A_visit_reading_only_ever_raises_the_vehicles_mileage(int current, int reading, int expected, bool changed)
    {
        var vehicle = new Vehicle { Mileage = current };

        var result = vehicle.RaiseMileageTo(reading);

        Assert.Equal(changed, result);
        Assert.Equal(expected, vehicle.Mileage);
    }

    // ---- "Done when: a car with 3 past visits shows all 3 with dates, mileage, and what was done" ----

    [Fact]
    public async Task A_vehicle_with_three_visits_shows_all_three_newest_first_with_what_was_done_and_totals()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "History Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "HIS 100");
        await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 1, 10), 80_000, JobStatus.Paid,
            ("Part", "Oil filter", 1, 120m), ("Labor", "Oil change", 1, 80m));                    // 200.00
        await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 9, 20), 95_500, JobStatus.Done,
            ("Part", "Front brake pads", 2, 300m), ("Labor", "Fit brake pads", 1.5m, 100m));      // 750.00
        await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 5, 2), 88_250, JobStatus.Paid,
            ("Part", "Air filter", 1, 150m));                                                    // 150.00
        var client = factory.CreateClientFor(userId, shopId);

        var html = Squash(await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}")));

        // All three visits, with their dates, mileage and what was done.
        foreach (var expected in new[]
                 {
                     "2026-01-10", "80,000", "Oil filter", "Oil change",
                     "2026-05-02", "88,250", "Air filter",
                     "2026-09-20", "95,500", "Front brake pads", "Fit brake pads",
                 })
        {
            Assert.Contains(expected, html);
        }

        // Newest first: September, then May, then January.
        var september = html.IndexOf("2026-09-20", StringComparison.Ordinal);
        var may = html.IndexOf("2026-05-02", StringComparison.Ordinal);
        var january = html.IndexOf("2026-01-10", StringComparison.Ordinal);
        Assert.True(september > 0 && september < may && may < january, "history must be newest first");

        // Each visit's total, and the sum of all of them.
        Assert.Contains("750.00", html);
        Assert.Contains("200.00", html);
        Assert.Contains("150.00", html);
        Assert.Contains("1,100.00", html);
        Assert.Contains("Visits: 3", html);
    }

    [Fact]
    public async Task Visits_on_the_same_day_show_the_later_job_card_first()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Same Day Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "SAM 100");
        var morning = await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 10, 1), 1000, JobStatus.Paid, ("Part", "Morning job", 1, 10m));
        var evening = await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 10, 1), 1010, JobStatus.Paid, ("Part", "Evening job", 1, 10m));
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

        Assert.True(evening > morning);
        Assert.True(html.IndexOf("Evening job", StringComparison.Ordinal) < html.IndexOf("Morning job", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_this_vehicles_visits_are_listed()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Two Cars Garage");
        var first = await SeedVehicleAsync(shopId, "ONE 111");
        var second = await SeedVehicleAsync(shopId, "TWO 222");
        await SeedJobAsync(shopId, first, new DateOnly(2026, 3, 3), 1000, JobStatus.Paid, ("Part", "Belongs to the first car", 1, 10m));
        await SeedJobAsync(shopId, second, new DateOnly(2026, 4, 4), 2000, JobStatus.Paid, ("Part", "Belongs to the second car", 1, 10m));
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{first}"));

        Assert.Contains("Belongs to the first car", html);
        Assert.DoesNotContain("Belongs to the second car", html);
    }

    [Fact]
    public async Task A_vehicle_without_visits_says_so_and_offers_to_open_a_job_card()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Empty History Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "NEW 100");
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

        Assert.Contains("No visits yet.", html);
        Assert.DoesNotContain("Visits:", html);
    }

    // ---- "Updating a job's mileage should update the vehicle's current mileage if it's higher" ----

    [Fact]
    public async Task Opening_a_job_card_with_a_higher_reading_raises_the_vehicles_mileage()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Mileage Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "MIL 100", mileage: 100_000);
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Jobs/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.DateIn", "2026-10-14"), ("Input.MileageAtVisit", "104500"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(104_500, await MileageOfAsync(shopId, vehicleId));

        // ...and the vehicle's page shows it.
        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));
        Assert.Contains("104,500", html);
    }

    [Fact]
    public async Task Opening_a_job_card_with_a_lower_reading_leaves_the_vehicles_mileage_alone()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Lower Mileage Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "LOW 100", mileage: 100_000);
        var client = factory.CreateClientFor(userId, shopId);

        await TestData.SubmitFormAsync(client, $"/Jobs/Create?vehicleId={vehicleId}",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.DateIn", "2026-10-14"), ("Input.MileageAtVisit", "95000"));

        Assert.Equal(100_000, await MileageOfAsync(shopId, vehicleId));
    }

    [Fact]
    public async Task Editing_a_job_cards_mileage_up_raises_the_vehicle_and_down_does_not_lower_it()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Edit Mileage Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "EDT 100", mileage: 50_000);
        var jobId = await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 10, 1), 50_000, JobStatus.InProgress);
        var client = factory.CreateClientFor(userId, shopId);

        await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{jobId}",
            ("Input.DateIn", "2026-10-01"), ("Input.MileageAtVisit", "51200"), ("Input.Notes", ""));
        Assert.Equal(51_200, await MileageOfAsync(shopId, vehicleId));

        // A later correction to a lower number changes the job card, not the vehicle.
        await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{jobId}",
            ("Input.DateIn", "2026-10-01"), ("Input.MileageAtVisit", "49000"), ("Input.Notes", ""));
        Assert.Equal(51_200, await MileageOfAsync(shopId, vehicleId));
        using var db = factory.CreateContextFor(shopId);
        Assert.Equal(49_000, (await db.JobCards.SingleAsync(j => j.Id == jobId)).MileageAtVisit);
    }

    // ---- tenant isolation ----

    [Fact]
    public async Task One_shops_service_history_never_shows_in_another_shops_vehicle_page()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        var vehicleA = await SeedVehicleAsync(shopA, "SAME 111");
        var vehicleB = await SeedVehicleAsync(shopB, "SAME 111"); // identical plate in the other shop
        await SeedJobAsync(shopA, vehicleA, new DateOnly(2026, 2, 2), 1000, JobStatus.Paid, ("Part", "Shop A private repair", 1, 999m));
        await SeedJobAsync(shopB, vehicleB, new DateOnly(2026, 3, 3), 2000, JobStatus.Paid, ("Part", "Shop B repair", 1, 5m));
        var clientB = factory.CreateClientFor(userB, shopB);

        // B cannot open A's vehicle at all...
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Vehicles/Details/{vehicleA}")).StatusCode);

        // ...and B's own page, same plate, shows only B's history.
        var html = await TestData.BodyAsync(await clientB.GetAsync($"/Vehicles/Details/{vehicleB}"));
        Assert.Contains("Shop B repair", html);
        Assert.DoesNotContain("Shop A private repair", html);
        Assert.DoesNotContain("999.00", html);
    }

    // ---- Arabic ----

    [Fact]
    public async Task The_history_is_translated_when_arabic_is_chosen()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic History Garage");
        var vehicleId = await SeedVehicleAsync(shopId, "ARB 100");
        await SeedJobAsync(shopId, vehicleId, new DateOnly(2026, 6, 6), 70_000, JobStatus.Paid, ("Part", "فلتر زيت", 1, 100m));
        var client = factory.CreateClientFor(userId, shopId);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("سجل الصيانة", html);
        Assert.Contains("ما تم تنفيذه", html);
        Assert.Contains("فلتر زيت", html);
        Assert.Contains("عدد الزيارات: 1", html);
    }

    // ---- helpers ----

    private static string Squash(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");

    private async Task<int> MileageOfAsync(int shopId, int vehicleId)
    {
        using var db = factory.CreateContextFor(shopId);
        return (await db.Vehicles.SingleAsync(v => v.Id == vehicleId)).Mileage;
    }

    private async Task<int> SeedVehicleAsync(int shopId, string plate, int mileage = 0)
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
        int shopId, int vehicleId, DateOnly dateIn, int mileage, JobStatus status,
        params (string Type, string Description, decimal Quantity, decimal UnitPrice)[] lines)
    {
        using var db = factory.CreateContextFor(shopId);
        var job = new JobCard { VehicleId = vehicleId, DateIn = dateIn, MileageAtVisit = mileage, Status = status };
        foreach (var line in lines)
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
