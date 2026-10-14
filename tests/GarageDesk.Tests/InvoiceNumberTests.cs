using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class InvoiceNumberTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private async Task<int> SeedJobAsync(int shopId, JobStatus status = JobStatus.Booked, DateOnly? dateIn = null)
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = "C", Phone = "+201000000000" };
        var vehicle = new Vehicle { Customer = customer, Plate = "ABC 1", PlateKey = "ABC1", Make = "Skoda", Model = "Fabia", Year = 2018, Mileage = 1000 };
        var job = new JobCard { Vehicle = vehicle, DateIn = dateIn ?? new DateOnly(2026, 1, 1), MileageAtVisit = 1000, Status = status };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Oil", Quantity = 1, UnitPrice = 100 });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task MoveAsync(HttpClient client, int jobId, JobStatus to)
    {
        var response = await TestData.SubmitFormAsync(client, $"/Jobs/Details/{jobId}?handler=Status&to={to}");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Number_is_given_on_first_Done_per_shop_and_both_shops_start_at_1()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "B");
        var a1 = await SeedJobAsync(shopA, JobStatus.InProgress);
        var a2 = await SeedJobAsync(shopA, JobStatus.InProgress);
        var b1 = await SeedJobAsync(shopB, JobStatus.InProgress);
        var clientA = factory.CreateClientFor(userA, shopA);
        var clientB = factory.CreateClientFor(userB, shopB);

        await MoveAsync(clientA, a2, JobStatus.Done);
        await MoveAsync(clientA, a1, JobStatus.Done);
        await MoveAsync(clientB, b1, JobStatus.Done);

        using var dbA = factory.CreateContextFor(shopA);
        Assert.Equal(1, (await dbA.JobCards.SingleAsync(j => j.Id == a2)).InvoiceNumber);
        Assert.Equal(2, (await dbA.JobCards.SingleAsync(j => j.Id == a1)).InvoiceNumber);
        using var dbB = factory.CreateContextFor(shopB);
        var jobB = await dbB.JobCards.SingleAsync(j => j.Id == b1);
        Assert.Equal(1, jobB.InvoiceNumber);
        Assert.Equal("INV-0001", jobB.InvoiceLabel);
    }

    [Fact]
    public async Task Number_never_changes_when_status_moves_back_or_on_to_Paid()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Immutable");
        var id = await SeedJobAsync(shop, JobStatus.InProgress);
        var client = factory.CreateClientFor(user, shop);

        await MoveAsync(client, id, JobStatus.Done);
        await MoveAsync(client, id, JobStatus.InProgress);
        await MoveAsync(client, id, JobStatus.Done);
        await MoveAsync(client, id, JobStatus.Paid);

        using var db = factory.CreateContextFor(shop);
        Assert.Equal(1, (await db.JobCards.SingleAsync()).InvoiceNumber);
        Assert.Equal(1, (await db.NumberSequences.SingleAsync()).LastValue);
    }

    [Fact]
    public async Task Invoice_page_shows_the_number_and_draft_before_issue()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Page");
        var draft = await SeedJobAsync(shop);
        var issued = await SeedJobAsync(shop, JobStatus.InProgress);
        var client = factory.CreateClientFor(user, shop);
        await MoveAsync(client, issued, JobStatus.Done);

        Assert.Contains("INV-0001", await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{issued}")));
        Assert.Contains("Draft", await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{draft}")));
        Assert.Contains("INV-0001", await TestData.BodyAsync(await client.GetAsync("/Jobs")));
    }

    [Fact]
    public async Task Concurrent_requests_get_distinct_gap_free_numbers()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Concurrent");
        var numbers = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var db = factory.CreateContextFor(shop);
            await using var tx = await db.Database.BeginTransactionAsync();
            var n = await DocumentNumbers.NextAsync(db, shop, DocumentNumbers.Invoice);
            await tx.CommitAsync();
            return n;
        }));

        Assert.Equal(Enumerable.Range(1, 12), numbers.Order());
    }

    [Fact]
    public async Task Backfill_numbers_issued_jobs_oldest_first_per_shop_and_seeds_counters()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "BackfillA");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "BackfillB");
        var late = await SeedJobAsync(shopA, JobStatus.Paid, new DateOnly(2026, 5, 1));
        var early = await SeedJobAsync(shopA, JobStatus.Done, new DateOnly(2026, 2, 1));
        var open = await SeedJobAsync(shopA, JobStatus.InProgress, new DateOnly(2026, 1, 1));
        var other = await SeedJobAsync(shopB, JobStatus.Paid, new DateOnly(2026, 7, 1));

        using (var db = factory.CreateContextFor(shopA))
        {
            await db.Database.ExecuteSqlRawAsync(InvoiceBackfill.AssignNumbersSql);
            await db.Database.ExecuteSqlRawAsync(InvoiceBackfill.SeedCountersSql);
        }

        using var a = factory.CreateContextFor(shopA);
        Assert.Equal(1, (await a.JobCards.SingleAsync(j => j.Id == early)).InvoiceNumber);
        Assert.Equal(2, (await a.JobCards.SingleAsync(j => j.Id == late)).InvoiceNumber);
        Assert.Null((await a.JobCards.SingleAsync(j => j.Id == open)).InvoiceNumber);
        Assert.Equal(2, (await a.NumberSequences.SingleAsync()).LastValue);
        using var b = factory.CreateContextFor(shopB);
        Assert.Equal(1, (await b.JobCards.SingleAsync(j => j.Id == other)).InvoiceNumber);
    }

    [Fact]
    public async Task Counters_are_isolated_between_shops()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "SeqA");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "SeqB");
        using (var db = factory.CreateContextFor(shopA)) await DocumentNumbers.NextAsync(db, shopA, DocumentNumbers.Invoice);

        using var other = factory.CreateContextFor(shopB);
        Assert.Empty(await other.NumberSequences.ToListAsync());
    }
}
