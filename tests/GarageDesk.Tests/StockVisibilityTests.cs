using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Tests;

/// <summary>
/// v1.4: who sees what. The cost price, the stock value and the profit are the owner's; a cashier sees sell prices and
/// quantities; a technician sees names and quantities and never a number of money.
/// </summary>
public class StockVisibilityTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private const string CostText = "61.37";
    private const string SellText = "95.25";

    private async Task<(string Owner, string Cashier, string Tech, int Shop, int Part)> ShopAsync(string name, decimal quantity = 10)
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, name);
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Tina Tech");
        var part = await StockTestData.SeedPartAsync(factory, shop, "Cabin filter", quantity, threshold: 3);
        return (owner, cashier, tech, shop, part);
    }

    // ---------------------------------------------------------------- the catalog pages

    [Fact]
    public async Task The_owner_sees_cost_and_stock_value_in_the_catalog()
    {
        var (owner, _, _, shop, _) = await ShopAsync("Vis Owner");

        var html = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Parts"));

        Assert.Contains("Cabin filter", html);
        Assert.Contains(CostText, html);
        Assert.Contains(SellText, html);
        Assert.Contains("Stock value (at cost)", html);
        Assert.Contains("613.70", html); // 10 x 61.37
        Assert.Contains("href=\"/Parts/Create\"", html);
    }

    [Fact]
    public async Task A_cashier_sees_names_quantities_and_sell_prices_but_never_cost_or_stock_value()
    {
        var (_, cashier, _, shop, part) = await ShopAsync("Vis Cashier");
        var client = factory.CreateClientFor(cashier, shop);

        var html = await TestData.BodyAsync(await client.GetAsync("/Parts"));

        Assert.Contains("Cabin filter", html);
        Assert.Contains(SellText, html);
        Assert.DoesNotContain(CostText, html);
        Assert.DoesNotContain("613.70", html);
        Assert.DoesNotContain("Stock value", html);
        Assert.DoesNotContain("Cost price", html);
        Assert.DoesNotContain("/Parts/Create", html);
        Assert.DoesNotContain("data-stock-value", html);

        // Writing is the owner's: forbidden, not hidden.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Parts/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Parts/Edit/{part}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Parts/Stock/{part}")).StatusCode);
    }

    [Fact]
    public async Task A_technician_cannot_open_the_catalog_pages()
    {
        var (_, _, tech, shop, part) = await ShopAsync("Vis Tech Catalog");
        var client = factory.CreateClientFor(tech, shop);

        foreach (var path in new[] { "/Parts", "/Parts/Create", $"/Parts/Edit/{part}", $"/Parts/Stock/{part}" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task The_menu_offers_parts_to_owner_and_cashier_but_not_the_technician()
    {
        var (owner, cashier, tech, shop, _) = await ShopAsync("Vis Menu");

        var ownerHtml = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Customers"));
        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync("/Customers"));
        var techHtml = await TestData.BodyAsync(await factory.CreateClientFor(tech, shop).GetAsync("/Work"));

        Assert.Contains("href=\"/Parts\"", ownerHtml);
        Assert.Contains("href=\"/Parts\"", cashierHtml);
        Assert.DoesNotContain("href=\"/Parts", techHtml);
    }

    // ---------------------------------------------------------------- the job card

    [Fact]
    public async Task Cost_and_profit_show_on_the_job_card_for_the_owner_only()
    {
        var (owner, cashier, _, shop, part) = await ShopAsync("Vis Job");
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        await StockTestData.AddLineAsync(factory.CreateClientFor(cashier, shop), job, part, "Cabin filter", "2");

        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync($"/Jobs/Details/{job}"));
        var ownerHtml = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync($"/Jobs/Details/{job}"));

        // The cashier used the part (and sees the sell price) but there is no trace of the cost or the profit.
        Assert.Contains("Cabin filter", cashierHtml);
        Assert.Contains(SellText, cashierHtml);
        Assert.DoesNotContain(CostText, cashierHtml);
        Assert.DoesNotContain("data-line-cost", cashierHtml);
        Assert.DoesNotContain("data-job-profit", cashierHtml);
        Assert.DoesNotContain("Part cost", cashierHtml);

        Assert.Contains(CostText, ownerHtml);
        Assert.Contains("data-job-profit", ownerHtml);
        // revenue 190.50 - cost 122.74 = 67.76
        Assert.Matches(@"data-profit-value[^>]*>.*?67\.76", ownerHtml.Replace("\n", " "));
    }

    [Fact]
    public async Task The_invoice_and_the_customers_public_page_never_show_the_cost()
    {
        var (owner, _, _, shop, part) = await ShopAsync("Vis Invoice");
        var job = await StockTestData.EmptyJobAsync(factory, shop, JobStatus.Booked);
        var client = factory.CreateClientFor(owner, shop);
        await StockTestData.AddLineAsync(client, job, part, "Cabin filter", "1");
        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=Status&to=InProgress");
        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=Status&to=Done");

        var invoice = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{job}"));
        Assert.Contains(SellText, invoice);
        Assert.DoesNotContain(CostText, invoice);

        var token = ShareTokens.Generate();
        using (var db = factory.CreateContextFor(shop))
        {
            db.ShareLinks.Add(new ShareLink
            {
                Kind = ShareLinkKind.Invoice, TargetId = job, TokenHash = ShareTokens.Hash(token), Language = "en",
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var publicHtml = await TestData.BodyAsync(await anonymous.GetAsync($"/s/{token}"));
        Assert.Contains(SellText, publicHtml);
        Assert.DoesNotContain(CostText, publicHtml);
    }

    // ---------------------------------------------------------------- the technician

    [Fact]
    public async Task The_technician_picks_a_catalog_part_by_name_and_quantity_and_sees_no_money()
    {
        var (_, _, tech, shop, part) = await ShopAsync("Vis Tech Pick");
        var job = await StockTestData.EmptyJobAsync(factory, shop, JobStatus.InProgress, assignedTo: tech);
        var client = factory.CreateClientFor(tech, shop);

        var page = await TestData.BodyAsync(await client.GetAsync($"/Work/Job/{job}"));
        Assert.Contains("Cabin filter (10 pcs)", page); // name and how many are on the shelf
        Assert.DoesNotContain(SellText, page);
        Assert.DoesNotContain(CostText, page);
        Assert.DoesNotContain("data-price", page);

        var add = await StockTestData.TechAddLineAsync(client, job, part, "", "2");
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        // Stock was taken and the line was priced from the catalog on the server...
        using (var db = factory.CreateContextFor(shop))
        {
            var line = await db.JobLines.SingleAsync();
            Assert.Equal("Cabin filter", line.Description);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(StockTestData.Sell, line.UnitPrice);
            Assert.Equal(StockTestData.Cost, line.UnitCost);
        }
        Assert.Equal(8m, await StockTestData.QuantityAsync(factory, shop, part));

        // ...but the technician's page still has no number of money anywhere.
        var after = await TestData.BodyAsync(await client.GetAsync($"/Work/Job/{job}"));
        Assert.Contains("Cabin filter", after);
        Assert.DoesNotContain(SellText, after);
        Assert.DoesNotContain("190.50", after);
        Assert.DoesNotContain(CostText, after);
    }

    [Fact]
    public async Task A_technician_can_take_back_their_own_catalog_part_but_not_a_line_the_front_desk_added()
    {
        var (_, cashier, tech, shop, part) = await ShopAsync("Vis Tech Remove");
        var job = await StockTestData.EmptyJobAsync(factory, shop, JobStatus.InProgress, assignedTo: tech);
        var techClient = factory.CreateClientFor(tech, shop);
        await StockTestData.TechAddLineAsync(techClient, job, part, "", "2");
        await StockTestData.AddLineAsync(factory.CreateClientFor(cashier, shop), job, part, "Cabin filter", "1");
        Assert.Equal(7m, await StockTestData.QuantityAsync(factory, shop, part));

        List<(int Id, string? AddedBy)> lines;
        using (var db = factory.CreateContextFor(shop))
        {
            lines = (await db.JobLines.OrderBy(l => l.Id).ToListAsync()).Select(l => (l.Id, l.AddedByUserId)).ToList();
        }
        var mine = lines.Single(l => l.AddedBy == tech).Id;
        var theirs = lines.Single(l => l.AddedBy == cashier).Id;

        var refused = await StockTestData.PostAsync(techClient, $"/Work/Job/{job}", $"/Work/Job/{job}?handler=RemoveLine&lineId={theirs}");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal(7m, await StockTestData.QuantityAsync(factory, shop, part));

        var removed = await StockTestData.PostAsync(techClient, $"/Work/Job/{job}", $"/Work/Job/{job}?handler=RemoveLine&lineId={mine}");
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Equal(9m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(9m, await StockTestData.LedgerSumAsync(factory, shop, part));
    }

    [Fact]
    public async Task A_technician_cannot_use_more_than_the_shelf_holds_and_sees_how_many_are_left()
    {
        var (_, _, tech, shop, part) = await ShopAsync("Vis Tech Short", quantity: 1);
        var job = await StockTestData.EmptyJobAsync(factory, shop, JobStatus.InProgress, assignedTo: tech);
        var client = factory.CreateClientFor(tech, shop);

        await StockTestData.TechAddLineAsync(client, job, part, "", "5");

        Assert.Equal(1m, await StockTestData.QuantityAsync(factory, shop, part));
        var page = await TestData.BodyAsync(await client.GetAsync($"/Work/Job/{job}"));
        Assert.Contains("Not enough in stock: 1 left of Cabin filter.", page);
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.JobLines.ToListAsync());
    }

    // ---------------------------------------------------------------- low stock and the dashboard

    [Fact]
    public async Task Low_stock_means_at_or_below_the_alert_and_no_alert_means_never_low()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Low");
        await StockTestData.SeedPartAsync(factory, shop, "At the line", quantity: 3, threshold: 3);
        await StockTestData.SeedPartAsync(factory, shop, "Above the line", quantity: 4, threshold: 3);
        await StockTestData.SeedPartAsync(factory, shop, "No alert", quantity: 0, threshold: null);
        await StockTestData.SeedPartAsync(factory, shop, "Archived low", quantity: 1, threshold: 5, active: false);

        var list = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Parts?show=low"));

        Assert.Contains("At the line", list);
        Assert.DoesNotContain("Above the line", list);
        Assert.DoesNotContain("No alert", list);
        Assert.DoesNotContain("Archived low", list);

        using var db = factory.CreateContextFor(shop);
        var summary = await StockSummary.LoadAsync(db, includeCost: true);
        Assert.Equal(1, summary.LowCount);
        Assert.Equal(3, summary.PartCount); // the archived part is not counted
    }

    [Fact]
    public async Task The_dashboard_shows_the_low_stock_card_to_the_cashier_and_the_stock_value_only_to_the_owner()
    {
        var (owner, cashier, tech, shop, _) = await ShopAsync("Vis Dashboard", quantity: 2); // 2 <= alert 3: low

        var ownerHtml = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/"));
        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync("/"));

        Assert.Matches(@"data-low-count[^>]*>\s*1\s*<", ownerHtml);
        Assert.Contains("data-stock-value", ownerHtml);
        Assert.Contains("122.74", ownerHtml); // 2 x 61.37

        Assert.Matches(@"data-low-count[^>]*>\s*1\s*<", cashierHtml);
        Assert.DoesNotContain("data-stock-value", cashierHtml);
        Assert.DoesNotContain("122.74", cashierHtml);
        Assert.DoesNotContain(CostText, cashierHtml);

        // The technician's home is My jobs, not the dashboard.
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateClientFor(tech, shop).GetAsync("/")).StatusCode);
    }

    // ---------------------------------------------------------------- the owner's forms

    [Fact]
    public async Task Adding_a_part_with_an_opening_quantity_writes_the_first_ledger_line_and_refuses_duplicates()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Create");
        var client = factory.CreateClientFor(owner, shop);

        var created = await TestData.SubmitFormAsync(client, "/Parts/Create",
            ("Input.Name", "Spark plug"), ("Input.Sku", "SP-1"), ("Input.Unit", "pcs"), ("Input.CostPrice", "30"),
            ("Input.SellPrice", "55.5"), ("Input.OpeningQuantity", "12"), ("Input.LowStockThreshold", "4"), ("Input.Supplier", "Cairo Parts"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        using (var db = factory.CreateContextFor(shop))
        {
            var part = await db.Parts.SingleAsync();
            Assert.Equal(12m, part.QuantityOnHand);
            var movement = await db.StockMovements.SingleAsync();
            Assert.Equal(StockMovementKind.Opening, movement.Kind);
            Assert.Equal(12m, movement.QuantityChange);
        }

        // Same name (any case) or same SKU: refused, nothing added.
        var sameName = await TestData.SubmitFormAsync(client, "/Parts/Create",
            ("Input.Name", "SPARK PLUG"), ("Input.Unit", "pcs"), ("Input.CostPrice", "1"), ("Input.SellPrice", "2"), ("Input.OpeningQuantity", "1"));
        var sameSku = await TestData.SubmitFormAsync(client, "/Parts/Create",
            ("Input.Name", "Another"), ("Input.Sku", "sp-1"), ("Input.Unit", "pcs"), ("Input.CostPrice", "1"), ("Input.SellPrice", "2"), ("Input.OpeningQuantity", "1"));
        Assert.Equal(HttpStatusCode.OK, sameName.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sameSku.StatusCode);
        using var verify = factory.CreateContextFor(shop);
        Assert.Equal(1, await verify.Parts.CountAsync());
    }

    [Fact]
    public async Task Receiving_stock_adds_to_the_shelf_updates_the_cost_and_can_record_the_expense()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Receive");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 2);
        var client = factory.CreateClientFor(owner, shop);

        var response = await StockTestData.PostAsync(client, $"/Parts/Stock/{part}", $"/Parts/Stock/{part}?handler=Purchase",
            ("Purchase.Quantity", "10"), ("Purchase.UnitCost", "70.10"), ("Purchase.Note", "Invoice 55"), ("Purchase.RecordExpense", "true"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Equal(12m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(12m, await StockTestData.LedgerSumAsync(factory, shop, part));
        using var db = factory.CreateContextFor(shop);
        Assert.Equal(70.10m, (await db.Parts.SingleAsync()).CostPrice);
        var purchase = await db.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.Purchase);
        Assert.Equal(70.10m, purchase.UnitCost);
        var expense = await db.Expenses.Include(e => e.Category).SingleAsync();
        Assert.Equal(701.00m, expense.Amount);
        Assert.Equal("Parts purchase", expense.Category!.Name);
    }

    [Fact]
    public async Task A_correction_below_zero_is_refused_and_a_valid_one_needs_a_reason()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Adjust");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 3);
        var client = factory.CreateClientFor(owner, shop);

        await StockTestData.PostAsync(client, $"/Parts/Stock/{part}", $"/Parts/Stock/{part}?handler=Adjust", ("Adjust.Change", "-5"), ("Adjust.Reason", "Lost"));
        await StockTestData.PostAsync(client, $"/Parts/Stock/{part}", $"/Parts/Stock/{part}?handler=Adjust", ("Adjust.Change", "-1"), ("Adjust.Reason", ""));
        Assert.Equal(3m, await StockTestData.QuantityAsync(factory, shop, part));

        await StockTestData.PostAsync(client, $"/Parts/Stock/{part}", $"/Parts/Stock/{part}?handler=Adjust", ("Adjust.Change", "-1"), ("Adjust.Reason", "Dropped one"));
        Assert.Equal(2m, await StockTestData.QuantityAsync(factory, shop, part));
        using var db = factory.CreateContextFor(shop);
        Assert.Equal("Dropped one", (await db.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.Adjustment)).Note);
    }

    [Fact]
    public async Task Editing_a_part_never_changes_the_cost_recorded_on_old_job_lines()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Edit");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 5);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);
        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "1");

        var saved = await TestData.SubmitFormAsync(client, $"/Parts/Edit/{part}",
            ("Input.Name", "Oil filter"), ("Input.Unit", "pcs"), ("Input.CostPrice", "99"), ("Input.SellPrice", "150"), ("Input.OpeningQuantity", "9999"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var db = factory.CreateContextFor(shop);
        var edited = await db.Parts.SingleAsync();
        Assert.Equal(99m, edited.CostPrice);
        Assert.Equal(4m, edited.QuantityOnHand); // the quantity is not editable here
        Assert.Equal(StockTestData.Cost, (await db.JobLines.SingleAsync()).UnitCost);
    }

    // ---------------------------------------------------------------- Arabic

    [Fact]
    public async Task The_parts_pages_render_in_arabic_and_every_stock_message_is_translated()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Vis Arabic");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 2, threshold: 5);
        var client = factory.CreateClientFor(owner, shop);
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar");

        var list = await TestData.BodyAsync(await client.GetAsync("/Parts"));
        Assert.Contains("dir=\"rtl\"", list);
        Assert.Contains("مخزون منخفض", list);
        var stock = await TestData.BodyAsync(await client.GetAsync($"/Parts/Stock/{part}"));
        Assert.Contains("حركات المخزون", stock);

        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
            using var scope = factory.Services.CreateScope();
            var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();
            foreach (var message in StockLedger.Messages.All)
            {
                Assert.False(localizer[message].ResourceNotFound, $"No Arabic text for: {message}");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
