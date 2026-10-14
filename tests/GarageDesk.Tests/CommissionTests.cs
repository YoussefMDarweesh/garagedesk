using System.Net;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>v1.3: commission percent per person, applied to the labor of paid job cards, by month.</summary>
public class CommissionTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static readonly DateOnly March = new(2026, 3, 10);

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(100, 10, 10)]
    [InlineData(53.35, 10, 5.34)]   // 5.335 rounds away from zero to the piastre
    [InlineData(80, 12.5, 10)]
    [InlineData(33.33, 33.33, 11.11)]
    [InlineData(1000, 0, 0)]
    [InlineData(1000, 100, 1000)]
    public void Commission_is_labor_times_percent_rounded_to_the_piastre(double labor, double percent, double expected) =>
        Assert.Equal((decimal)expected, Commissions.Due((decimal)labor, (decimal)percent));

    [Fact]
    public async Task Only_paid_labor_of_the_month_assigned_to_the_person_counts()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Commission Shop");
        var tina = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Tina Ten", commissionPercent: 10);
        var omar = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Omar Zero");

        // Counts: two paid March jobs of Tina, labor 80 + 50 (the 123.45 part is never commissionable).
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, tina, plate: "COM 1", laborPrice: 80, dateIn: March);
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, tina, plate: "COM 2", laborPrice: 50, dateIn: new DateOnly(2026, 3, 31));
        // Does not count: not paid yet, another month, nobody assigned.
        await TestData.SeedJobAsync(factory, shop, JobStatus.Done, tina, plate: "COM 3", laborPrice: 1000, dateIn: March);
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, tina, plate: "COM 4", laborPrice: 1000, dateIn: new DateOnly(2026, 4, 1));
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, tina, plate: "COM 5", laborPrice: 1000, dateIn: new DateOnly(2026, 2, 28));
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, null, plate: "COM 6", laborPrice: 1000, dateIn: March);
        // Omar has paid work but a 0% commission.
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, omar, plate: "COM 7", laborPrice: 200, dateIn: March);

        using var db = factory.CreateContextFor(shop);
        var lines = await Commissions.MonthAsync(db, shop, 2026, 3);

        var t = lines.Single(l => l.Name == "Tina Ten");
        Assert.Equal(2, t.Jobs);
        Assert.Equal(130m, t.LaborTotal);
        Assert.Equal(13m, t.Due);
        var o = lines.Single(l => l.Name == "Omar Zero");
        Assert.Equal(1, o.Jobs);
        Assert.Equal(200m, o.LaborTotal);
        Assert.Equal(0m, o.Due);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task The_report_page_shows_the_numbers_and_the_rule_to_the_owner_only()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Report Shop");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Rita Report", commissionPercent: 15);
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, tech, plate: "REP 1", laborPrice: 200, dateIn: March);
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, null, plate: "REP 2", dateIn: March);

        var html = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Reports/Commissions?month=2026-03"));

        Assert.Contains("Rita Report", html);
        Assert.Contains("30.00", html); // 15% of 200
        Assert.Contains("200.00", html);
        Assert.Contains("How commission is worked out", html);
        Assert.Contains("have no technician", html); // the unassigned paid job is mentioned
        Assert.Contains("2026-03", html);

        // A different month has none of March's money (she is still listed, at 0, because she has a percent).
        var other = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Reports/Commissions?month=2026-05"));
        Assert.DoesNotContain("30.00", other);
        Assert.DoesNotContain("200.00", other);
        Assert.Contains("2026-05", other);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(cashier, shop).GetAsync("/Reports/Commissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(tech, shop).GetAsync("/Reports/Commissions")).StatusCode);
    }

    [Fact]
    public async Task The_report_never_includes_people_or_jobs_of_another_shop()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Report A");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "Report B");
        var techB = await TestData.CreateStaffAsync(factory, shopB, ShopRole.Technician, fullName: "Bea Elsewhere", commissionPercent: 50);
        await TestData.SeedJobAsync(factory, shopB, JobStatus.Paid, techB, plate: "ELSE 1", laborPrice: 999, dateIn: March);

        var html = await TestData.BodyAsync(await factory.CreateClientFor(ownerA, shopA).GetAsync("/Reports/Commissions?month=2026-03"));

        Assert.DoesNotContain("Bea Elsewhere", html);
        Assert.DoesNotContain("999.00", html);
    }

    [Fact]
    public async Task Only_the_owner_sets_commission_and_only_for_their_own_shop_within_zero_to_hundred()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Set Commission");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Other Commission");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);
        var stranger = await TestData.CreateStaffAsync(factory, otherShop, ShopRole.Technician);
        var ownerClient = factory.CreateClientFor(owner, shop);

        async Task<decimal> PercentAsync(string id)
        {
            using var scope = factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id))!.CommissionPercent;
        }

        async Task<HttpResponseMessage> SetAsync(HttpClient client, string id, string percent)
        {
            var token = await TestData.TokenAsync(ownerClient, "/Staff");
            return await client.PostAsync($"/Staff?handler=Commission&id={id}",
                new FormUrlEncodedContent([new("percent", percent), new("__RequestVerificationToken", token)]));
        }

        Assert.Equal(HttpStatusCode.Redirect, (await SetAsync(ownerClient, tech, "12.5")).StatusCode);
        Assert.Equal(12.5m, await PercentAsync(tech));

        await SetAsync(ownerClient, tech, "101");
        await SetAsync(ownerClient, tech, "-1");
        Assert.Equal(12.5m, await PercentAsync(tech));

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(ownerClient, stranger, "50")).StatusCode);
        Assert.Equal(0m, await PercentAsync(stranger));

        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(factory.CreateClientFor(cashier, shop), tech, "99")).StatusCode);
        Assert.Equal(12.5m, await PercentAsync(tech));
    }
}
