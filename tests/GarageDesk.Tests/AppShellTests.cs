using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public class AppShellTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static DateOnly Today => ShopClock.Today();

    // ---- the shell ----

    [Fact]
    public async Task A_signed_in_shop_owner_gets_the_app_shell_with_every_section_in_the_navigation()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Shell Garage");
        var client = factory.CreateClientFor(userId, shopId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, "Shell Garage");

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("class=\"app-shell\"", html);
        Assert.Contains("class=\"sidebar\"", html);
        Assert.Contains("class=\"topbar\"", html);
        Assert.Contains("class=\"tabbar\"", html);
        foreach (var path in new[] { "/", "/Customers", "/Vehicles", "/Jobs", "/Reminders", "/ShopSettings" })
        {
            Assert.Matches($"rail-link[^>]*href=\"{Regex.Escape(path)}\"", html);
        }

        // The top bar carries search, the reminders bell, language and theme switches and the user menu.
        Assert.Contains("type=\"search\"", html);
        Assert.Contains("lang-switch", html);
        Assert.Contains("js-theme-toggle", html);
        Assert.Contains("Log out", html);
    }

    [Theory]
    [InlineData("/Customers", "/Customers")]
    [InlineData("/Vehicles", "/Vehicles")]
    [InlineData("/Jobs", "/Jobs")]
    [InlineData("/Reminders", "/Reminders")]
    [InlineData("/ShopSettings", "/ShopSettings")]
    [InlineData("/", "/")]
    public async Task Only_the_current_section_is_marked_active_in_the_navigation(string url, string activeHref)
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, $"Active Garage {Guid.NewGuid():N}");
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync(url));

        var active = Regex.Matches(html, "<a class=\"rail-link active\"[^>]*href=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal([activeHref], active);
        Assert.Contains("aria-current=\"page\"", html);
    }

    [Fact]
    public async Task In_arabic_the_page_is_right_to_left_and_the_shell_is_still_present()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic Shell Garage");
        var client = factory.CreateClientFor(userId, shopId);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\"", html);
        Assert.Contains("class=\"sidebar\"", html);
        Assert.Contains("bootstrap.rtl.min.css", html);
        Assert.Contains("لوحة التحكم", html);
        Assert.Contains("العملاء", html);
    }

    [Fact]
    public void The_stylesheet_uses_logical_properties_only_so_right_to_left_mirrors_by_itself()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "css", "site.css"));
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline); // ignore comments

        var physical = Regex.Matches(css,
                @"(?<![-\w])(margin-left|margin-right|padding-left|padding-right|border-left|border-right|left|right)\s*:")
            .Select(m => m.Value).Distinct().ToList();

        Assert.True(physical.Count == 0, "Physical properties found (use inline-start/end instead): " + string.Join(", ", physical));
    }

    [Fact]
    public async Task Visitors_get_the_public_pages_not_the_shell()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var landing = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Contains("class=\"public\"", landing);
        Assert.DoesNotContain("class=\"sidebar\"", landing);
        Assert.Contains("landing-hero", landing);

        var login = await TestData.BodyAsync(await client.GetAsync("/Identity/Account/Login"));
        Assert.Contains("auth-split", login);
        Assert.Contains("auth-panel", login);
        Assert.DoesNotContain("class=\"sidebar\"", login);
    }

    [Fact]
    public async Task A_signed_in_user_without_a_shop_does_not_get_the_shell()
    {
        var userId = await TestData.CreateUserWithoutShopAsync(factory);
        var client = factory.CreateClientFor(userId);

        var html = await TestData.BodyAsync(await client.GetAsync("/CreateShop"));

        Assert.DoesNotContain("class=\"sidebar\"", html);
        Assert.Contains("public-header", html);
    }

    [Fact]
    public async Task The_bell_shows_how_many_reminders_are_due_or_overdue()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Bell Garage");
        await SeedReminderAsync(shopId, Today.AddDays(-3));
        await SeedReminderAsync(shopId, Today.AddDays(5));
        await SeedReminderAsync(shopId, Today.AddDays(60)); // not due yet
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/Customers"));

        Assert.Matches(@"class=""count num"">2<", html);
    }

    // ---- the dashboard: real numbers, scoped to the shop ----

    [Fact]
    public async Task The_dashboard_adds_up_this_months_paid_job_cards_and_compares_with_last_month()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Numbers Garage");
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        await SeedJobAsync(shopId, thisMonth, JobStatus.Paid, ("Part", 2, 250m));          // 500.00
        await SeedJobAsync(shopId, thisMonth, JobStatus.Paid, ("Labor", 1, 200m));         // 200.00  -> 700.00 this month
        await SeedJobAsync(shopId, thisMonth, JobStatus.Done, ("Part", 1, 999m));          // not paid: not revenue
        await SeedJobAsync(shopId, thisMonth.AddMonths(-1), JobStatus.Paid, ("Part", 1, 300m)); // last month: 300.00
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));
        var flat = Regex.Replace(html, @"\s+", " ");

        Assert.Matches(@"Revenue this month</span>.*?stat-value"">\s*<span class=""ltr"">700\.00</span>", flat);
        Assert.Contains("+133%", flat); // (700 - 300) / 300
        Assert.Matches(@"Open job cards</span>.*?stat-value"">\s*1\s*</p>", flat);
    }

    [Fact]
    public async Task The_dashboard_of_one_shop_never_shows_another_shops_totals()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);

        // A is busy and rich...
        for (var i = 0; i < 4; i++)
        {
            await SeedJobAsync(shopA, thisMonth, JobStatus.Paid, ("Part", 1, 12_345m));
        }
        await SeedCustomersAsync(shopA, 6);
        await SeedReminderAsync(shopA, Today.AddDays(-9));

        // ...B has one small paid job.
        await SeedJobAsync(shopB, thisMonth, JobStatus.Paid, ("Labor", 1, 80m));
        var clientB = factory.CreateClientFor(userB, shopB);

        var html = await TestData.BodyAsync(await clientB.GetAsync("/"));
        var flat = Regex.Replace(html, @"\s+", " ");

        // B's numbers: revenue 80, one customer (the seeded owner of its vehicle), one vehicle...
        Assert.Matches(@"Revenue this month</span>.*?stat-value"">\s*<span class=""ltr"">80\.00</span>", flat);
        Assert.Matches(@"Customers</span>.*?stat-value"">\s*1\s*</p>", flat);
        Assert.Matches(@"Vehicles</span>.*?stat-value"">\s*1\s*</p>", flat);
        // ...and nothing of A's: not its revenue, not its reminder.
        Assert.DoesNotContain("49,380", flat);
        Assert.DoesNotContain("12,345", flat);
        Assert.Contains("All caught up", flat);

        // The chart data embedded in the page is B's too.
        var json = JsonDocument.Parse(Regex.Match(html, @"<script type=""application/json"">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);
        var months = json.RootElement.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, months.Count);
        Assert.Equal($"{Today.Year:0000}-{Today.Month:00}", months[^1].GetProperty("key").GetString());
        Assert.Equal(80m, months[^1].GetProperty("revenue").GetDecimal());
        Assert.Equal(1, months[^1].GetProperty("jobs").GetInt32());
        Assert.Equal(80m, months.Sum(m => m.GetProperty("revenue").GetDecimal()));
    }

    [Fact]
    public async Task The_recent_service_history_lists_the_newest_job_cards_first()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Recent Garage");
        await SeedJobAsync(shopId, Today.AddDays(-20), JobStatus.Paid, ("Part", 1, 10m), description: "Old job");
        await SeedJobAsync(shopId, Today.AddDays(-1), JobStatus.Done, ("Part", 1, 10m), description: "Fresh job");
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.True(html.IndexOf("Fresh job", StringComparison.Ordinal) < html.IndexOf("Old job", StringComparison.Ordinal));
        Assert.Contains("Recent service history", html);
    }

    // ---- empty states ----

    [Fact]
    public async Task A_brand_new_shop_sees_friendly_empty_states_with_a_next_step()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, $"Empty Garage {Guid.NewGuid():N}");
        var client = factory.CreateClientFor(userId, shopId);

        var home = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Contains("Getting started", home);
        Assert.Contains("Add your first customer", home);
        Assert.Contains("Your activity will show up here", home);
        Assert.Contains("All caught up", home);
        Assert.Contains("No service history yet", home);
        Assert.DoesNotContain("data-chart", home); // nothing to chart yet

        var expectations = new[]
        {
            ("/Customers", "No customers yet.", "/Customers/Create"),
            ("/Vehicles", "No vehicles yet.", "/Vehicles/Create"),
            ("/Jobs", "No job cards yet.", "/Jobs/Create"),
            ("/Reminders", "No reminders yet.", "/Reminders/Create"),
        };
        foreach (var (url, message, action) in expectations)
        {
            var page = await TestData.BodyAsync(await client.GetAsync(url));
            Assert.Contains(message, page);
            Assert.Contains($"href=\"{action}\"", page);
        }
    }

    // ---- paging ----

    [Fact]
    public async Task Long_lists_are_paged_and_keep_the_search_in_the_links()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, $"Paging Garage {Guid.NewGuid():N}");
        await SeedCustomersAsync(shopId, 25, prefix: "Pager");
        var client = factory.CreateClientFor(userId, shopId);

        var first = await TestData.BodyAsync(await client.GetAsync("/Customers"));
        Assert.Contains("Showing 1–20 of 25", first);
        Assert.Contains("Pager Person 01", first);
        Assert.DoesNotContain("Pager Person 25", first);
        Assert.Contains("href=\"/Customers?p=2\"", first);

        var second = await TestData.BodyAsync(await client.GetAsync("/Customers?p=2"));
        Assert.Contains("Showing 21–25 of 25", second);
        Assert.Contains("Pager Person 25", second);
        Assert.DoesNotContain("Pager Person 01", second);

        // A page number out of range lands on the last page instead of an empty one.
        var beyond = await TestData.BodyAsync(await client.GetAsync("/Customers?p=99"));
        Assert.Contains("Showing 21–25 of 25", beyond);

        // Searching keeps the text in the pager links.
        var searched = await TestData.BodyAsync(await client.GetAsync("/Customers?q=Pager"));
        Assert.Contains("href=\"/Customers?q=Pager&amp;p=2\"", searched.Replace("&#x2B;", "+").Replace("&", "&amp;").Replace("&amp;amp;", "&amp;"));
    }

    // ---- toasts and confirms ----

    [Fact]
    public async Task Messages_are_rendered_as_toasts_and_risky_actions_ask_first()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Toast Garage");
        int jobId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var customer = new Customer { Name = "Toaster", Phone = "+201000000000" };
            var vehicle = new Vehicle { Customer = customer, Plate = "TST 1", PlateKey = "TST1", Make = "Kia", Model = "Rio", Year = 2020 };
            var job = new JobCard { Vehicle = vehicle, DateIn = Today, Status = JobStatus.Done };
            job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Filter", Quantity = 1, UnitPrice = 10 });
            db.JobCards.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var page = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Contains("data-confirm=\"Remove this line from the job card?\"", page);           // removing a line
        Assert.Contains("data-confirm=\"Mark this job card as paid?", page);                     // the irreversible step

        // An error becomes a toast source, not a banner.
        var token = await TestData.TokenAsync(client, $"/Jobs/Details/{jobId}");
        await client.PostAsync($"/Jobs/Details/{jobId}?handler=Status&to=Booked", new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        var after = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));
        Assert.Matches(@"data-toast=""danger""[^>]*>[^<]*That status change is not allowed\.", after);
    }

    // ---- the vehicle page: status tiles from stored data ----

    [Fact]
    public async Task The_vehicle_tiles_measure_kilometres_and_days_since_the_last_service()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Tiles Garage");
        int vehicleId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var customer = new Customer { Name = "Tiler", Phone = "+201000000000" };
            var vehicle = new Vehicle
            {
                Customer = customer, Plate = "TIL 1", PlateKey = "TIL1", Make = "Skoda", Model = "Octavia", Year = 2020, Mileage = 120_000,
            };
            vehicle.Customer = customer;
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;

            db.JobCards.Add(new JobCard
            {
                VehicleId = vehicleId, DateIn = Today.AddDays(-45), MileageAtVisit = 115_000, Status = JobStatus.Paid,
            });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientFor(userId, shopId);

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}")), @"\s+", " ");

        Assert.Contains("5,000 km", html);                       // 120,000 - 115,000
        Assert.Contains("45 days", html);
        Assert.Contains("aria-valuenow=\"50\"", html);           // 5,000 of 10,000 km
        Assert.Contains("aria-valuenow=\"25\"", html);           // 45 of 180 days
    }

    [Fact]
    public async Task A_vehicle_that_was_never_serviced_says_so_instead_of_inventing_numbers()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Never Serviced Garage");
        int vehicleId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var vehicle = new Vehicle
            {
                Customer = new Customer { Name = "Newbie", Phone = "+201000000000" },
                Plate = "NEW 9", PlateKey = "NEW9", Make = "Kia", Model = "Rio", Year = 2024, Mileage = 500,
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

        Assert.Contains("No service recorded yet", html);
        Assert.Contains("aria-valuenow=\"0\"", html);
    }

    // ---- helpers ----

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GarageDesk.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private async Task SeedReminderAsync(int shopId, DateOnly due)
    {
        using var db = factory.CreateContextFor(shopId);
        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = $"Owner {Guid.NewGuid():N}"[..14], Phone = "+201000000000" },
            Plate = $"R{Random.Shared.Next(1000, 9999)}", PlateKey = $"R{Random.Shared.Next(1000, 9999)}",
            Make = "Kia", Model = "Rio", Year = 2020,
        };
        db.Reminders.Add(new Reminder { Vehicle = vehicle, Type = "Oil change", DueDate = due });
        await db.SaveChangesAsync();
    }

    private async Task SeedCustomersAsync(int shopId, int count, string prefix = "Customer")
    {
        using var db = factory.CreateContextFor(shopId);
        for (var i = 1; i <= count; i++)
        {
            db.Customers.Add(new Customer { Name = $"{prefix} Person {i:00}", Phone = $"+2010000{i:00000}" });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedJobAsync(
        int shopId, DateOnly dateIn, JobStatus status, (string Type, decimal Quantity, decimal UnitPrice) line,
        string description = "Work")
    {
        using var db = factory.CreateContextFor(shopId);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync();
        if (vehicle is null)
        {
            vehicle = new Vehicle
            {
                Customer = new Customer { Name = "Seed Owner", Phone = "+201000000000" },
                Plate = "SEED 1", PlateKey = "SEED1", Make = "Kia", Model = "Rio", Year = 2020,
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
        }

        var job = new JobCard { VehicleId = vehicle.Id, DateIn = dateIn, Status = status };
        job.Lines.Add(new JobLine
        {
            Type = Enum.Parse<JobLineType>(line.Type), Description = description, Quantity = line.Quantity, UnitPrice = line.UnitPrice,
        });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
    }
}
