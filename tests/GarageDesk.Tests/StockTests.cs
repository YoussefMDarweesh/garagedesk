using System.Net;
using GarageDesk.Web.Data.Migrations;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GarageDesk.Tests;

/// <summary>Shared helpers for the v1.4 tests.</summary>
public static class StockTestData
{
    public const decimal Cost = 61.37m;
    public const decimal Sell = 95.25m;

    public static async Task<int> SeedPartAsync(
        GarageDeskWebFactory factory, int shop, string name = "Oil filter", decimal quantity = 10, decimal cost = Cost,
        decimal sell = Sell, decimal? threshold = null, bool active = true, string? sku = null)
    {
        using var db = factory.CreateContextFor(shop);
        var part = new Part
        {
            Name = name, Sku = sku, Unit = "pcs", CostPrice = cost, SellPrice = sell, LowStockThreshold = threshold,
            IsActive = active, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Parts.Add(part);
        if (quantity > 0)
        {
            StockLedger.Apply(db, part, StockMovementKind.Opening, quantity, null, note: "Opening stock");
        }

        await db.SaveChangesAsync();
        return part.Id;
    }

    public static async Task<decimal> QuantityAsync(GarageDeskWebFactory factory, int shop, int partId)
    {
        using var db = factory.CreateContextFor(shop);
        return (await db.Parts.SingleAsync(p => p.Id == partId)).QuantityOnHand;
    }

    public static async Task<decimal> LedgerSumAsync(GarageDeskWebFactory factory, int shop, int partId)
    {
        using var db = factory.CreateContextFor(shop);
        return (await db.StockMovements.Where(m => m.PartId == partId).Select(m => m.QuantityChange).ToListAsync()).Sum();
    }

    public static Task<HttpResponseMessage> AddLineAsync(
        HttpClient client, int job, int? partId, string description = "", string quantity = "1", string price = "0", string type = "Part") =>
        TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=AddLine",
            ("NewLine.Type", type), ("NewLine.Description", description), ("NewLine.Quantity", quantity),
            ("NewLine.UnitPrice", price), ("NewLine.PartId", partId?.ToString() ?? ""));

    public static Task<HttpResponseMessage> TechAddLineAsync(HttpClient client, int job, int? partId, string description = "", string quantity = "1") =>
        TestData.SubmitFormAsync(client, $"/Work/Job/{job}?handler=AddLine",
            ("NewLine.Type", "Part"), ("NewLine.Description", description), ("NewLine.Quantity", quantity),
            ("NewLine.PartId", partId?.ToString() ?? ""));

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string url, params (string, string)[] fields)
    {
        var token = await TestData.TokenAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).Append(new("__RequestVerificationToken", token))));
    }

    /// <summary>An empty job card (no lines) for one new vehicle. Returns the job id.</summary>
    public static async Task<int> EmptyJobAsync(GarageDeskWebFactory factory, int shop, JobStatus status = JobStatus.Booked, string? assignedTo = null, string plate = "STK 100")
    {
        using var db = factory.CreateContextFor(shop);
        var customer = new Customer { Name = "Stock Customer", Phone = "+201000000055" };
        var vehicle = new Vehicle { Customer = customer, Plate = plate, PlateKey = plate.Replace(" ", ""), Make = "Kia", Model = "Rio", Year = 2019, Mileage = 1 };
        var job = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 3, 1), MileageAtVisit = 1, Status = status, AssignedToUserId = assignedTo };
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }
}

/// <summary>v1.4 part 1: the parts catalog, the stock ledger, the low-stock list and the job card part picker.</summary>
public class StockTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---------------------------------------------------------------- the job card lifecycle

    [Fact]
    public async Task Picking_a_catalog_part_takes_stock_records_cost_and_uses_the_catalog_price()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Pick");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 10);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);

        var response = await StockTestData.AddLineAsync(client, job, part, description: "Oil filter", quantity: "2");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var db = factory.CreateContextFor(shop);
        var line = await db.JobLines.SingleAsync(l => l.JobCardId == job);
        Assert.Equal(part, line.PartId);
        Assert.Equal(JobLineType.Part, line.Type);
        Assert.Equal(StockTestData.Sell, line.UnitPrice); // no price typed: the catalog sell price
        Assert.Equal(StockTestData.Cost, line.UnitCost);  // the cost is a snapshot on the line
        Assert.Equal(8m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(8m, await StockTestData.LedgerSumAsync(factory, shop, part)); // quantity == sum of movements

        var use = await db.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.JobUse);
        Assert.Equal(-2m, use.QuantityChange);
        Assert.Equal(8m, use.QuantityAfter);
        Assert.Equal(job, use.JobCardId);
        Assert.Contains(await db.JobAuditEntries.ToListAsync(), a => a.Kind == JobAuditKind.PartUsed && a.Detail == "Oil filter|2");
    }

    [Fact]
    public async Task A_typed_price_wins_over_the_catalog_price_and_free_text_never_touches_stock()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Price");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 5);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);

        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "1", "120.50");
        await StockTestData.AddLineAsync(client, job, null, "Wiper blade", "2", "40");

        using var db = factory.CreateContextFor(shop);
        var lines = await db.JobLines.Where(l => l.JobCardId == job).OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(120.50m, lines[0].UnitPrice);
        Assert.Null(lines[1].PartId);
        Assert.Null(lines[1].UnitCost);
        Assert.Equal(4m, await StockTestData.QuantityAsync(factory, shop, part)); // only the catalog line took stock
    }

    [Fact]
    public async Task Removing_the_line_puts_the_stock_back_and_the_ledger_stays_consistent()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Remove");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 10);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);
        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "3");

        int lineId;
        using (var db = factory.CreateContextFor(shop))
        {
            lineId = (await db.JobLines.SingleAsync()).Id;
        }

        await StockTestData.PostAsync(client, $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=RemoveLine&lineId={lineId}");

        Assert.Equal(10m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(10m, await StockTestData.LedgerSumAsync(factory, shop, part));
        using var verify = factory.CreateContextFor(shop);
        Assert.Empty(await verify.JobLines.ToListAsync());
        Assert.Contains(await verify.StockMovements.ToListAsync(), m => m.Kind == StockMovementKind.JobReturn && m.QuantityChange == 3m);
        Assert.Contains(await verify.JobAuditEntries.ToListAsync(), a => a.Kind == JobAuditKind.PartReturned);
    }

    [Fact]
    public async Task Finishing_a_job_does_not_take_stock_a_second_time_and_a_paid_job_stays_locked()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Finish");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 10);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);
        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "2");
        Assert.Equal(8m, await StockTestData.QuantityAsync(factory, shop, part));

        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=Status&to=InProgress");
        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=Status&to=Done");
        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{job}?handler=Status&to=Paid");
        Assert.Equal(8m, await StockTestData.QuantityAsync(factory, shop, part));

        // Paid is final: nothing can be added or taken off, so stock cannot move.
        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "1");
        int lineId;
        using (var db = factory.CreateContextFor(shop))
        {
            Assert.Equal(JobStatus.Paid, (await db.JobCards.SingleAsync(j => j.Id == job)).Status);
            lineId = (await db.JobLines.SingleAsync()).Id;
        }

        await StockTestData.PostAsync(client, $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=RemoveLine&lineId={lineId}");

        Assert.Equal(8m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(8m, await StockTestData.LedgerSumAsync(factory, shop, part));
        using var verify = factory.CreateContextFor(shop);
        Assert.Single(await verify.JobLines.ToListAsync());
    }

    // ---------------------------------------------------------------- not enough stock

    [Fact]
    public async Task A_part_cannot_be_used_beyond_the_shelf_unless_the_owner_allows_negative_stock()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Negative");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 2);
        var job = await StockTestData.EmptyJobAsync(factory, shop);
        var client = factory.CreateClientFor(owner, shop);

        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "3");
        Assert.Equal(2m, await StockTestData.QuantityAsync(factory, shop, part));
        using (var db = factory.CreateContextFor(shop))
        {
            Assert.Empty(await db.JobLines.ToListAsync());
        }

        var allow = await StockTestData.PostAsync(client, "/Parts", "/Parts?handler=AllowNegative&allow=true");
        Assert.Equal(HttpStatusCode.Redirect, allow.StatusCode);

        await StockTestData.AddLineAsync(client, job, part, "Oil filter", "3");
        Assert.Equal(-1m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(-1m, await StockTestData.LedgerSumAsync(factory, shop, part));
    }

    [Fact]
    public async Task Only_the_owner_can_change_the_negative_stock_setting()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Stock Setting");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var client = factory.CreateClientFor(cashier, shop);

        var response = await StockTestData.PostAsync(client, "/Parts", "/Parts?handler=AllowNegative&allow=true");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.False((await db.Shops.SingleAsync()).AllowNegativeStock);
    }

    [Fact]
    public async Task An_archived_part_cannot_be_picked()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Archived");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 5, active: false);
        var job = await StockTestData.EmptyJobAsync(factory, shop);

        await StockTestData.AddLineAsync(factory.CreateClientFor(owner, shop), job, part, "Oil filter");

        Assert.Equal(5m, await StockTestData.QuantityAsync(factory, shop, part));
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.JobLines.ToListAsync());
    }

    // ---------------------------------------------------------------- estimates

    [Fact]
    public async Task An_estimate_with_a_catalog_part_takes_stock_once_when_it_becomes_a_job_card()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Estimate");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 5);
        var vehicleJob = await StockTestData.EmptyJobAsync(factory, shop, plate: "EST 100");
        int estimateId;
        using (var db = factory.CreateContextFor(shop))
        {
            var vehicleId = (await db.JobCards.SingleAsync(j => j.Id == vehicleJob)).VehicleId;
            var estimate = new Estimate { Number = 1, VehicleId = vehicleId, CreatedOn = new DateOnly(2026, 3, 1) };
            db.Estimates.Add(estimate);
            await db.SaveChangesAsync();
            estimateId = estimate.Id;
        }

        var client = factory.CreateClientFor(owner, shop);
        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{estimateId}?handler=AddLine",
            ("NewLine.Type", "Part"), ("NewLine.Description", ""), ("NewLine.Quantity", "2"), ("NewLine.UnitPrice", "0"),
            ("NewLine.PartId", part.ToString()));

        // Quoting does not touch stock.
        Assert.Equal(5m, await StockTestData.QuantityAsync(factory, shop, part));
        using (var db = factory.CreateContextFor(shop))
        {
            var quoted = await db.EstimateLines.SingleAsync();
            Assert.Equal(part, quoted.PartId);
            Assert.Equal("Oil filter", quoted.Description); // the part's name was used
            Assert.Equal(StockTestData.Sell, quoted.UnitPrice);
        }

        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{estimateId}?handler=Convert");
        Assert.Equal(3m, await StockTestData.QuantityAsync(factory, shop, part));

        // A double click (or a second convert) goes to the existing job card and takes nothing more.
        await TestData.SubmitFormAsync(client, $"/Estimates/Details/{estimateId}?handler=Convert");
        Assert.Equal(3m, await StockTestData.QuantityAsync(factory, shop, part));
        Assert.Equal(3m, await StockTestData.LedgerSumAsync(factory, shop, part));

        using var verify = factory.CreateContextFor(shop);
        var jobLine = await verify.JobLines.SingleAsync(l => l.PartId == part);
        Assert.Equal(StockTestData.Cost, jobLine.UnitCost);
        Assert.Equal(2, await verify.JobCards.CountAsync()); // the seeded job and the converted one
    }

    [Fact]
    public async Task Converting_an_estimate_is_refused_when_the_shelf_cannot_cover_it()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Stock Estimate Short");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 1);
        var seeded = await StockTestData.EmptyJobAsync(factory, shop, plate: "EST 200");
        int estimateId;
        using (var db = factory.CreateContextFor(shop))
        {
            var vehicleId = (await db.JobCards.SingleAsync(j => j.Id == seeded)).VehicleId;
            var estimate = new Estimate { Number = 1, VehicleId = vehicleId, CreatedOn = new DateOnly(2026, 3, 1) };
            estimate.Lines.Add(new EstimateLine { Type = JobLineType.Part, Description = "Oil filter", Quantity = 4, UnitPrice = 90, PartId = part });
            db.Estimates.Add(estimate);
            await db.SaveChangesAsync();
            estimateId = estimate.Id;
        }

        await TestData.SubmitFormAsync(factory.CreateClientFor(owner, shop), $"/Estimates/Details/{estimateId}?handler=Convert");

        Assert.Equal(1m, await StockTestData.QuantityAsync(factory, shop, part));
        using var verify = factory.CreateContextFor(shop);
        Assert.Equal(1, await verify.JobCards.CountAsync());
        Assert.Null((await verify.Estimates.SingleAsync()).JobCardId);
    }

    // ---------------------------------------------------------------- the model

    [Fact]
    public async Task The_stock_ledger_is_append_only()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Stock Append");
        var part = await StockTestData.SeedPartAsync(factory, shop, quantity: 4);

        using (var db = factory.CreateContextFor(shop))
        {
            var movement = await db.StockMovements.SingleAsync(m => m.PartId == part);
            movement.QuantityChange = 400;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var db = factory.CreateContextFor(shop))
        {
            db.StockMovements.Remove(await db.StockMovements.SingleAsync(m => m.PartId == part));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Two_changes_to_the_same_quantity_at_once_do_not_overwrite_each_other()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Stock Concurrent");
        var partId = await StockTestData.SeedPartAsync(factory, shop, quantity: 10);

        using var first = factory.CreateContextFor(shop);
        using var second = factory.CreateContextFor(shop);
        var a = await first.Parts.SingleAsync(p => p.Id == partId);
        var b = await second.Parts.SingleAsync(p => p.Id == partId);

        StockLedger.Apply(first, a, StockMovementKind.JobUse, -3, null);
        StockLedger.Apply(second, b, StockMovementKind.JobUse, -4, null);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(7m, await StockTestData.QuantityAsync(factory, shop, partId));
        Assert.Equal(7m, await StockTestData.LedgerSumAsync(factory, shop, partId));
    }

    [Fact]
    public async Task Existing_shops_and_job_lines_get_safe_values_when_the_migration_runs()
    {
        var up = new AddStockExpensesCashClose().UpOperations.OfType<AddColumnOperation>().ToList();

        var negative = up.Single(o => o.Table == "Shops" && o.Name == "AllowNegativeStock");
        Assert.Equal(false, negative.DefaultValue); // existing shops: negative stock is not allowed
        foreach (var column in up.Where(o => o.Table is "JobLines" or "EstimateLines"))
        {
            Assert.True(column.IsNullable, $"{column.Table}.{column.Name} must be nullable so old lines stay free text");
        }

        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- tenant safety

    [Fact]
    public async Task Another_shop_cannot_see_use_or_change_our_parts()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Parts A");
        var (ownerB, shopB) = await TestData.CreateOwnerAsync(factory, "Parts B");
        var partA = await StockTestData.SeedPartAsync(factory, shopA, "Secret brake pads", quantity: 7);
        var jobB = await StockTestData.EmptyJobAsync(factory, shopB, plate: "ISO 200");
        var clientB = factory.CreateClientFor(ownerB, shopB);

        // The list and the pages of shop A's part: nothing, or 404.
        Assert.DoesNotContain("Secret brake pads", await TestData.BodyAsync(await clientB.GetAsync("/Parts")));
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Parts/Stock/{partA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Parts/Edit/{partA}")).StatusCode);

        // Using it on shop B's own job card by guessing the id does nothing.
        await StockTestData.AddLineAsync(clientB, jobB, partA, "Secret brake pads", "1");
        Assert.Equal(7m, await StockTestData.QuantityAsync(factory, shopA, partA));
        using (var dbB = factory.CreateContextFor(shopB))
        {
            Assert.Empty(await dbB.JobLines.ToListAsync());
            Assert.Empty(await dbB.Parts.ToListAsync());
            Assert.Empty(await dbB.StockMovements.ToListAsync());
        }

        // Receiving stock or archiving through shop B's session cannot touch shop A either.
        var token = await TestData.TokenAsync(clientB, "/Parts");
        var receive = await clientB.PostAsync($"/Parts/Stock/{partA}?handler=Purchase", new FormUrlEncodedContent(
            [new("Purchase.Quantity", "50"), new("Purchase.UnitCost", "1"), new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.NotFound, receive.StatusCode);
        Assert.Equal(7m, await StockTestData.QuantityAsync(factory, shopA, partA));
        Assert.NotNull(ownerA);
    }

    [Fact]
    public async Task The_database_context_refuses_to_write_a_part_or_movement_for_another_shop()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Ctx A");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "Ctx B");
        var partA = await StockTestData.SeedPartAsync(factory, shopA, quantity: 1);

        using var asB = factory.CreateContextFor(shopB);
        asB.Parts.Add(new Part { ShopId = shopA, Name = "Smuggled", Unit = "pcs", CreatedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<InvalidOperationException>(() => asB.SaveChangesAsync());

        using var asB2 = factory.CreateContextFor(shopB);
        Assert.Null(await asB2.Parts.FirstOrDefaultAsync(p => p.Id == partA));
        Assert.Empty(await asB2.StockMovements.ToListAsync());
    }
}
