using System.Net;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public class DemoDataTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    // Each test gets its own database (and so its own factory), because the seed command always makes the same shop.
    private static async Task<(GarageDeskWebFactory Factory, int ShopId, string UserId, string Output)> SeedAsync(
        string? password = "Demo-Pass-1")
    {
        var factory = new GarageDeskWebFactory();
        var settings = new Dictionary<string, string?> { ["Demo:Email"] = "demo@example.com", ["Demo:Password"] = password };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var output = new StringWriter();

        var exitCode = await DemoDataCommand.RunAsync(factory.Services, configuration, output);
        Assert.Equal(0, exitCode);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByEmailAsync("demo@example.com");
        Assert.NotNull(owner);
        return (factory, owner.ShopId!.Value, owner.Id, output.ToString());
    }

    [Fact]
    public async Task The_command_creates_Demo_Motors_with_10_customers_and_15_cars_of_the_five_makes()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shopId);

        var shop = await db.Shops.SingleAsync();
        Assert.Equal("Demo Motors", shop.Name);

        Assert.Equal(10, await db.Customers.CountAsync());
        var vehicles = await db.Vehicles.ToListAsync();
        Assert.Equal(15, vehicles.Count);
        Assert.Equal(
            ["Hyundai", "Kia", "Skoda", "Toyota", "Volkswagen"],
            vehicles.Select(v => v.Make).Distinct().Order().ToArray());
        Assert.All(vehicles.GroupBy(v => v.Make), g => Assert.Equal(3, g.Count()));
        Assert.Equal(15, vehicles.Select(v => v.PlateKey).Distinct().Count());
        Assert.All(vehicles, v => Assert.Equal(PlateNormalizer.Key(v.Plate), v.PlateKey));
    }

    [Fact]
    public async Task Every_row_belongs_to_the_demo_shop_and_phones_are_in_the_stored_format()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shopId);

        Assert.All(await db.Customers.ToListAsync(), c =>
        {
            Assert.Equal(shopId, c.ShopId);
            Assert.True(EgyptianPhone.TryNormalize(c.Phone, out var normalized));
            Assert.Equal(normalized, c.Phone);
        });
        Assert.All(await db.Vehicles.ToListAsync(), v => Assert.Equal(shopId, v.ShopId));
        Assert.All(await db.JobCards.ToListAsync(), j => Assert.Equal(shopId, j.ShopId));
        Assert.All(await db.JobLines.ToListAsync(), l => Assert.Equal(shopId, l.ShopId));
        Assert.All(await db.Reminders.ToListAsync(), r => Assert.Equal(shopId, r.ShopId));
    }

    [Fact]
    public async Task Every_car_has_history_and_no_visit_is_above_the_cars_current_mileage()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shopId);

        var vehicles = await db.Vehicles.ToListAsync();
        var jobs = await db.JobCards.Include(j => j.Lines).ToListAsync();

        Assert.All(vehicles, v => Assert.Contains(jobs, j => j.VehicleId == v.Id));
        Assert.All(jobs, j =>
        {
            Assert.NotEmpty(j.Lines);
            Assert.True(j.Total > 0);
            Assert.True(j.MileageAtVisit <= vehicles.Single(v => v.Id == j.VehicleId).Mileage);
        });

        // Visits get further along the odometer as time goes on.
        foreach (var group in jobs.GroupBy(j => j.VehicleId))
        {
            var byDate = group.OrderBy(j => j.DateIn).ThenBy(j => j.Id).ToList();
            Assert.Equal(byDate.Select(j => j.MileageAtVisit).Order(), byDate.Select(j => j.MileageAtVisit));
        }
    }

    [Fact]
    public async Task The_history_covers_every_status_and_the_open_jobs_are_recent()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shopId);

        var jobs = await db.JobCards.ToListAsync();
        Assert.Equivalent(
            new[] { JobStatus.Booked, JobStatus.InProgress, JobStatus.Done, JobStatus.Paid },
            jobs.Select(j => j.Status).Distinct());

        // Paid history reaches back about a year, so the dashboard chart has several months to draw.
        var paidMonths = jobs.Where(j => j.Status == JobStatus.Paid).Select(j => (j.DateIn.Year, j.DateIn.Month)).Distinct();
        Assert.True(paidMonths.Count() >= 8);
    }

    [Fact]
    public async Task The_dashboard_has_overdue_and_due_soon_reminders_and_a_finished_one_stays_off_it()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shopId);

        var today = ShopClock.Today();
        var due = await db.Reminders.Include(r => r.Vehicle).DueOrOverdue(today).ToListAsync();
        var statuses = due.Select(r => r.StatusFor(today, r.Vehicle!.Mileage)).ToList();

        Assert.True(statuses.Count(s => s == ReminderStatus.Overdue) >= 3);
        Assert.True(statuses.Count(s => s == ReminderStatus.DueSoon) >= 3);
        Assert.DoesNotContain(due, r => r.IsDone);

        // There is a finished reminder, and some that are not due yet, so the list is not everything.
        var all = await db.Reminders.Include(r => r.Vehicle).ToListAsync();
        Assert.Contains(all, r => r.IsDone);
        Assert.Contains(all, r => !r.IsDone && r.StatusFor(today, r.Vehicle!.Mileage) == ReminderStatus.Upcoming);
    }

    [Fact]
    public async Task Running_it_twice_changes_nothing()
    {
        var (factory, shopId, _, _) = await SeedAsync();
        using var _f = factory;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Email"] = "demo@example.com", ["Demo:Password"] = "Another-Pass-2" })
            .Build();
        var output = new StringWriter();
        Assert.Equal(0, await DemoDataCommand.RunAsync(factory.Services, configuration, output));

        Assert.Contains("already has data", output.ToString());
        using var db = factory.CreateContextFor(shopId);
        Assert.Equal(10, await db.Customers.CountAsync());
        Assert.Equal(15, await db.Vehicles.CountAsync());

        // The existing login keeps its password; the second run does not reset it.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = (await users.FindByEmailAsync("demo@example.com"))!;
        Assert.True(await users.CheckPasswordAsync(owner, "Demo-Pass-1"));
        Assert.False(await users.CheckPasswordAsync(owner, "Another-Pass-2"));
    }

    [Fact]
    public async Task Without_a_configured_password_a_random_one_is_printed_and_works()
    {
        var (factory, _, _, output) = await SeedAsync(password: null);
        using var _f = factory;

        var line = output.Split('\n').Single(l => l.StartsWith("Password: "));
        var password = line["Password: ".Length..].Split(' ')[0];

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = (await users.FindByEmailAsync("demo@example.com"))!;
        Assert.True(await users.CheckPasswordAsync(owner, password));
    }

    [Fact]
    public async Task Another_shop_cannot_see_any_of_the_demo_data()
    {
        var (factory, _, _, _) = await SeedAsync();
        using var _f = factory;
        var (otherUser, otherShop) = await TestData.CreateOwnerAsync(factory, "Other Garage");
        var client = factory.CreateClientFor(otherUser, otherShop);

        using var db = factory.CreateContextFor(otherShop);
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.Vehicles.ToListAsync());
        Assert.Empty(await db.JobCards.ToListAsync());
        Assert.Empty(await db.Reminders.ToListAsync());

        var customers = await TestData.BodyAsync(await client.GetAsync("/Customers"));
        Assert.DoesNotContain("Ahmed Hassan", customers);
    }

    [Fact]
    public async Task The_demo_owner_sees_the_data_in_the_app()
    {
        var (factory, shopId, userId, _) = await SeedAsync();
        using var _f = factory;
        var client = factory.CreateClientFor(userId, shopId);

        var customers = await client.GetAsync("/Customers");
        Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
        Assert.Contains("Ahmed Hassan", await TestData.BodyAsync(customers));

        // A plate typed any way is found.
        var search = await TestData.BodyAsync(await client.GetAsync("/Vehicles?q=%D8%B3%D9%86%D9%85+4821"));
        Assert.Contains("Octavia", search);

        var home = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Contains("Demo Motors", home);
        Assert.Contains("Oil change", home);
    }
}

public class DemoMoneyTests
{
    [Fact]
    public async Task Demo_shop_has_invoice_numbers_payments_an_estimate_and_a_balance()
    {
        using var factory = new GarageDeskWebFactory();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Email"] = "demo@example.com", ["Demo:Password"] = "Demo-Pass-1" }).Build();
        Assert.Equal(0, await Web.Services.DemoDataCommand.RunAsync(factory.Services, configuration, new StringWriter()));
        using var scope = factory.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync("demo@example.com"))!;

        using var db = factory.CreateContextFor(owner.ShopId!.Value);
        var jobs = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).ToListAsync();
        var issued = jobs.Where(j => j.Status is JobStatus.Done or JobStatus.Paid).ToList();
        Assert.Equal(Enumerable.Range(1, issued.Count), issued.Select(j => j.InvoiceNumber!.Value).Order());
        Assert.Equal(issued.Count, (await db.NumberSequences.SingleAsync(s => s.Kind == "Invoice")).LastValue);
        Assert.All(jobs.Where(j => j.Status == JobStatus.Paid), j => Assert.Equal(j.Total, j.PaidTotal));
        Assert.Contains(jobs, j => j.Status == JobStatus.Done && j.Balance > 0 && j.PaidTotal > 0);
        Assert.Equal("EST-0001", (await db.Estimates.SingleAsync()).Label);
        Assert.True((await Web.Services.OutstandingBalances.LoadAsync(db)).Count > 0);
    }
}
