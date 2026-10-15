using System.Text.RegularExpressions;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Tests;

/// <summary>The v1.4 queries must translate to PostgreSQL too (production), not just SQLite (tests). Nothing is executed.</summary>
public class StockMoneyPostgresTranslationTests
{
    private static ApplicationDbContext NewPostgresContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .Options,
            new FakeCurrentShop(1));

    [Fact]
    public void The_parts_catalog_and_picker_queries_translate_and_state_the_shop()
    {
        using var db = NewPostgresContext();

        var picker = db.Parts.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Unit, p.QuantityOnHand }).Take(1000).ToQueryString();
        Assert.Contains("\"ShopId\"", picker);
        Assert.DoesNotContain("CostPrice", picker); // the technician's picker never reads the cost (or the sell price)
        Assert.DoesNotContain("SellPrice", picker);

        var movements = db.StockMovements.AsNoTracking().Where(m => m.PartId == 5)
            .OrderByDescending(m => m.AtUtc).ThenByDescending(m => m.Id).Take(200).ToQueryString();
        Assert.Contains("\"StockMovements\"", movements);
        Assert.Contains("\"ShopId\"", movements);
    }

    [Fact]
    public void The_money_queries_translate_and_state_the_shop()
    {
        using var db = NewPostgresContext();
        var first = new DateOnly(2026, 3, 1);
        var next = first.AddMonths(1);

        var payments = db.Payments.AsNoTracking().Where(p => p.PaidOn >= first && p.PaidOn < next)
            .Select(p => new { p.Method, p.Amount }).ToQueryString();
        Assert.Contains("\"Payments\"", payments);
        Assert.Contains("\"ShopId\"", payments);

        var expenses = db.Expenses.AsNoTracking().Where(e => e.Date >= first && e.Date < next)
            .Select(e => new { e.Amount, Category = e.Category!.Name }).ToQueryString();
        Assert.Contains("\"ExpenseCategories\"", expenses);
        Assert.Contains("\"ShopId\"", expenses);

        var jobs = db.JobCards.AsNoTracking()
            .Where(j => j.Status == JobStatus.Paid && j.DateIn >= first && j.DateIn < next)
            .Include(j => j.Vehicle).Include(j => j.Lines).OrderBy(j => j.DateIn).ThenBy(j => j.Id).ToQueryString();
        Assert.Contains("\"UnitCost\"", jobs);

        var closes = db.CashCloses.AsNoTracking().Where(c => c.Date < first)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).Select(c => new { c.CountedCash }).ToQueryString();
        Assert.Contains("\"CashCloses\"", closes);
        Assert.Contains("\"ShopId\"", closes);
    }
}

/// <summary>The demo shop shows off the v1.4 features: a stock ledger that adds up, low stock, expenses and closes.</summary>
public class StockMoneyDemoTests
{
    private static async Task<(GarageDeskWebFactory Factory, int Shop, string Owner)> SeedAsync()
    {
        var factory = new GarageDeskWebFactory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Email"] = "demo@example.com", ["Demo:Password"] = "Demo-Pass-1" }).Build();
        Assert.Equal(0, await DemoDataCommand.RunAsync(factory.Services, configuration, new StringWriter()));
        using var scope = factory.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync("demo@example.com"))!;
        return (factory, owner.ShopId!.Value, owner.Id);
    }

    [Fact]
    public async Task The_demo_shop_has_a_catalog_whose_quantities_equal_their_ledgers_and_some_parts_running_low()
    {
        var (factory, shop, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shop);

        var parts = await db.Parts.ToListAsync();
        var movements = await db.StockMovements.ToListAsync();
        Assert.True(parts.Count >= 8);
        Assert.All(parts, p =>
        {
            Assert.Equal(p.QuantityOnHand, movements.Where(m => m.PartId == p.Id).Sum(m => m.QuantityChange));
            Assert.True(p.QuantityOnHand >= 0);
            Assert.Equal(shop, p.ShopId);
        });
        Assert.True(parts.Count(p => p.IsLow) >= 2);

        // Each part's ledger is a running balance in time order.
        foreach (var group in movements.GroupBy(m => m.PartId))
        {
            var running = 0m;
            foreach (var m in group.OrderBy(m => m.AtUtc).ThenBy(m => m.Kind).ThenBy(m => m.Id))
            {
                running += m.QuantityChange;
                Assert.Equal(running, m.QuantityAfter);
            }
        }

        // History lines that match the catalog point at it and carry its cost; free-text lines remain.
        var lines = await db.JobLines.ToListAsync();
        Assert.Contains(lines, l => l.PartId != null && l.UnitCost > 0);
        Assert.Contains(lines, l => l.Type == JobLineType.Part && l.PartId == null);
    }

    [Fact]
    public async Task The_demo_shop_has_expenses_for_four_months_and_cash_closes_with_a_shortage()
    {
        var (factory, shop, _) = await SeedAsync();
        using var _f = factory;
        using var db = factory.CreateContextFor(shop);

        var expenses = await db.Expenses.ToListAsync();
        Assert.True(expenses.Select(e => (e.Date.Year, e.Date.Month)).Distinct().Count() >= 4);
        Assert.True((await db.ExpenseCategories.CountAsync()) == 5);

        var closes = await db.CashCloses.OrderBy(c => c.Date).ToListAsync();
        Assert.Equal(3, closes.Count);
        Assert.All(closes, c => Assert.Equal(c.ExpectedCash + c.Difference, c.CountedCash));
        Assert.Equal(-20m, closes[^1].Difference);
    }

    [Fact]
    public async Task The_demo_owner_can_open_the_new_pages_and_download_an_export()
    {
        var (factory, shop, owner) = await SeedAsync();
        using var _f = factory;
        var client = factory.CreateClientFor(owner, shop);

        foreach (var path in new[] { "/Parts", "/Parts?show=low", "/Money/Profit", "/Money/Expenses", "/Money/CashClose", "/Money/Export", "/" })
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        }

        var today = ShopClock.Today();
        var csv = await TestData.BodyAsync(await client.GetAsync(
            $"/Money/Export?handler=Download&kind=jobs&from={today.AddDays(-300):yyyy-MM-dd}&to={today.AddDays(5):yyyy-MM-dd}"));
        Assert.Contains("Job card,Invoice number", csv);
    }
}

/// <summary>
/// The completeness test only sees literal L["..."] and localizer["..."]. The v1.4 pages also pass literal messages to
/// Fail(...) and SaveAsync(...) helpers; this finds those too so no message is left in English.
/// </summary>
public partial class StockMoneyTranslationTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Fact]
    public void Messages_passed_to_the_page_helpers_have_arabic_texts()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "GarageDesk.sln")))
        {
            root = Path.GetDirectoryName(root);
        }
        Assert.NotNull(root);

        var pages = Path.Combine(root!, "src", "GarageDesk.Web", "Pages");
        var keys = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(pages, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match m in HelperCall().Matches(File.ReadAllText(file)))
            {
                keys.Add(m.Groups[1].Value.Replace("\\\"", "\""));
            }
        }

        Assert.True(keys.Count > 10);
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
            using var scope = factory.Services.CreateScope();
            var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<GarageDesk.Web.SharedResource>>();
            var missing = keys.Where(k => localizer[k].ResourceNotFound).ToList();
            Assert.True(missing.Count == 0, "Missing Arabic texts:\n  " + string.Join("\n  ", missing));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [GeneratedRegex(@"\b(?:Fail|SaveAsync)\((?:[^""\n)]*?,\s*)?""((?:[^""\\]|\\.)*)""")]
    private static partial Regex HelperCall();
}
