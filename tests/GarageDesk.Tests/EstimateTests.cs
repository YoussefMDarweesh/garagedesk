using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class EstimateTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private async Task<int> SeedVehicleAsync(int shopId, string plate = "EST 1")
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = "Quote Customer", Phone = "+201000000003" };
        var vehicle = new Vehicle { Customer = customer, Plate = plate, PlateKey = plate.Replace(" ", ""), Make = "Skoda", Model = "Octavia", Year = 2018, Mileage = 98_000 };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle.Id;
    }

    private static async Task<int> CreateEstimateAsync(HttpClient client, int vehicleId)
    {
        var response = await TestData.SubmitFormAsync(client, "/Estimates/Create",
            ("Input.VehicleId", vehicleId.ToString()), ("Input.ValidUntil", "2026-12-31"), ("Input.Notes", "Quote notes"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(response.Headers.Location!.ToString().Split('/').Last());
    }

    private static Task AddLineAsync(HttpClient client, int id, string type, string description, string qty, string price) =>
        TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=AddLine",
            ("NewLine.Type", type), ("NewLine.Description", description), ("NewLine.Quantity", qty), ("NewLine.UnitPrice", price));

    [Fact]
    public async Task Estimates_are_numbered_per_shop_and_totals_follow_the_shared_rules()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "EstA");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "EstB");
        var vehicleA = await SeedVehicleAsync(shopA);
        var vehicleB = await SeedVehicleAsync(shopB, "EST 2");
        var clientA = factory.CreateClientFor(userA, shopA);
        var clientB = factory.CreateClientFor(userB, shopB);

        var first = await CreateEstimateAsync(clientA, vehicleA);
        var second = await CreateEstimateAsync(clientA, vehicleA);
        var otherShop = await CreateEstimateAsync(clientB, vehicleB);
        await AddLineAsync(clientA, first, "Part", "Brake pads", "2", "150.50");
        await AddLineAsync(clientA, first, "Labor", "Fit", "1.5", "200");

        using var dbA = factory.CreateContextFor(shopA);
        var estimates = await dbA.Estimates.Include(e => e.Lines).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal([1, 2], estimates.Select(e => e.Number));
        Assert.Equal("EST-0001", estimates[0].Label);
        Assert.Equal(601.00m, estimates[0].Total);
        using var dbB = factory.CreateContextFor(shopB);
        Assert.Equal(1, (await dbB.Estimates.SingleAsync(e => e.Id == otherShop)).Number);

        var page = (await TestData.BodyAsync(await clientA.GetAsync($"/Estimates/Details/{first}"))).Replace("\n", " ");
        Assert.Matches(@"data-estimate-total[^>]*>.*?601\.00", page);
        Assert.Contains("EST-0002", await TestData.BodyAsync(await clientA.GetAsync("/Estimates")));
    }

    [Fact]
    public async Task Convert_creates_one_job_card_with_the_same_lines_and_links_both_ways()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Convert");
        var vehicle = await SeedVehicleAsync(shop);
        var client = factory.CreateClientFor(user, shop);
        var id = await CreateEstimateAsync(client, vehicle);
        await AddLineAsync(client, id, "Part", "Oil", "1", "300");
        await AddLineAsync(client, id, "Labor", "Oil change", "0.5", "200");

        var first = await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Convert");
        var second = await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Convert");

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.Include(j => j.Lines).SingleAsync();
        var estimate = await db.Estimates.SingleAsync();
        Assert.Equal(EstimateStatus.Accepted, estimate.Status);
        Assert.Equal(job.Id, estimate.JobCardId);
        Assert.Equal(estimate.Id, job.EstimateId);
        Assert.Equal(vehicle, job.VehicleId);
        Assert.Equal(JobStatus.Booked, job.Status);
        Assert.Equal(400m, job.Total);
        Assert.Equal(["Oil", "Oil change"], job.Lines.OrderBy(l => l.Id).Select(l => l.Description));

        // The estimate cannot be edited any more, and both pages point at each other.
        await AddLineAsync(client, id, "Part", "Late", "1", "1");
        using (var db2 = factory.CreateContextFor(shop)) Assert.Equal(2, await db2.EstimateLines.CountAsync());
        Assert.Contains("data-estimate-job", await TestData.BodyAsync(await client.GetAsync($"/Estimates/Details/{id}")));
        Assert.Contains("data-from-estimate", await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{job.Id}")));
    }

    [Fact]
    public async Task Status_flow_and_whatsapp_link()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "EstStatus");
        var vehicle = await SeedVehicleAsync(shop);
        var client = factory.CreateClientFor(user, shop);
        var id = await CreateEstimateAsync(client, vehicle);
        await AddLineAsync(client, id, "Part", "Filter", "1", "100");

        var page = await TestData.BodyAsync(await client.GetAsync($"/Estimates/Details/{id}"));
        // Since v1.2 the button posts to the Share handler, which makes a private link and opens wa.me with it.
        Assert.Contains("data-estimate-whatsapp", page);
        var share = await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Share");
        Assert.Equal(HttpStatusCode.Redirect, share.StatusCode);
        Assert.StartsWith("https://wa.me/201000000003?text=", share.Headers.Location!.ToString());
        Assert.Contains("%2Fs%2F", share.Headers.Location.ToString());

        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Status&to=Sent");
        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Status&to=Accepted"); // not allowed by hand
        using (var db = factory.CreateContextFor(shop)) Assert.Equal(EstimateStatus.Sent, (await db.Estimates.SingleAsync()).Status);

        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{id}?handler=Status&to=Declined");
        using (var db = factory.CreateContextFor(shop)) Assert.Equal(EstimateStatus.Declined, (await db.Estimates.SingleAsync()).Status);
    }

    [Fact]
    public async Task Other_shops_get_404_and_cannot_see_or_convert_our_estimate()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "EstIsoA");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "EstIsoB");
        var vehicle = await SeedVehicleAsync(shopA);
        var clientA = factory.CreateClientFor(userA, shopA);
        var clientB = factory.CreateClientFor(userB, shopB);
        var id = await CreateEstimateAsync(clientA, vehicle);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Estimates/Details/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Estimates/Print/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Estimates/Create?vehicleId={vehicle}")).StatusCode);
        Assert.DoesNotContain("EST-0001", await TestData.BodyAsync(await clientB.GetAsync("/Estimates")));

        var token = await TestData.TokenAsync(clientB, "/Estimates");
        var convert = await clientB.PostAsync($"/Estimates/Details/{id}?handler=Convert",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.NotFound, convert.StatusCode);
        using var dbB = factory.CreateContextFor(shopB);
        Assert.Empty(await dbB.JobCards.ToListAsync());
    }

    [Fact]
    public async Task Printable_estimate_renders_with_the_paper_and_arabic_rtl_works()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "EstPrint");
        var vehicle = await SeedVehicleAsync(shop);
        var client = factory.CreateClientFor(user, shop);
        var id = await CreateEstimateAsync(client, vehicle);
        await AddLineAsync(client, id, "Part", "Filter", "1", "100");

        var html = await TestData.BodyAsync(await client.GetAsync($"/Estimates/Print/{id}"));
        Assert.Contains("class=\"invoice\"", html);
        Assert.Contains("EST-0001", html);
        Assert.DoesNotContain("data-invoice-balance", html);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Estimates/Print/{id}");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c%3Den%7Cuic%3Dar");
        var arabic = await TestData.BodyAsync(await client.SendAsync(request));
        Assert.Contains("dir=\"rtl\"", arabic);
        Assert.Contains("عرض سعر", arabic);
    }
}
