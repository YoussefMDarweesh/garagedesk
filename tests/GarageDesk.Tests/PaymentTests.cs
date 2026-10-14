using System.Net;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class PaymentTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // Lines: 1 x 100.005 rounds to 100.01 (half away from zero) + 1 x 50 => total 150.01
    private async Task<int> SeedJobAsync(int shopId, JobStatus status = JobStatus.Done, decimal price = 100.005m, string plate = "PAY 1")
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = "Owes Money", Phone = "+201000000001" };
        var vehicle = new Vehicle { Customer = customer, Plate = plate, PlateKey = plate.Replace(" ", ""), Make = "Kia", Model = "Rio", Year = 2019, Mileage = 1 };
        var job = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 3, 1), MileageAtVisit = 1, Status = status };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Part", Quantity = 1, UnitPrice = price });
        job.Lines.Add(new JobLine { Type = JobLineType.Labor, Description = "Labor", Quantity = 1, UnitPrice = 50 });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient c, int job, string amount, string method = "Cash") =>
        TestData.SubmitFormAsync(c, $"/Jobs/Details/{job}?handler=AddPayment",
            ("NewPayment.Amount", amount), ("NewPayment.Method", method), ("NewPayment.Note", ""));

    private static async Task<HttpResponseMessage> VoidAsync(HttpClient c, int job, int paymentId)
    {
        var token = await TestData.TokenAsync(c, $"/Jobs/Details/{job}");
        return await c.PostAsync($"/Jobs/Details/{job}?handler=VoidPayment&paymentId={paymentId}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
    }

    [Fact]
    public async Task Balance_rounds_to_the_piastre_and_partial_payments_reduce_it()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Partial");
        var id = await SeedJobAsync(shop);
        var client = factory.CreateClientFor(user, shop);

        await PayAsync(client, id, "50");
        await PayAsync(client, id, "20.25", "Wallet");

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).SingleAsync();
        Assert.Equal(150.01m, job.Total);
        Assert.Equal(70.25m, job.PaidTotal);
        Assert.Equal(79.76m, job.Balance);
        Assert.Equal(PaymentState.Partial, job.PaymentState);
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Contains("79.76", await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{id}")));
    }

    [Fact]
    public async Task Overpayment_and_zero_are_rejected()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Over");
        var id = await SeedJobAsync(shop);
        var client = factory.CreateClientFor(user, shop);

        await PayAsync(client, id, "150.02");
        await PayAsync(client, id, "0");

        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Paying_the_whole_balance_of_a_done_job_makes_it_paid_and_locked()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "AutoPaid");
        var id = await SeedJobAsync(shop);
        var client = factory.CreateClientFor(user, shop);

        await PayAsync(client, id, "100");
        await PayAsync(client, id, "50.01");

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).SingleAsync();
        Assert.Equal(JobStatus.Paid, job.Status);
        Assert.Equal(0, job.Balance);

        // Locked: no more payments, and nothing can be voided.
        await PayAsync(client, id, "1");
        await VoidAsync(client, id, job.Payments.Max(p => p.Id));
        using var db2 = factory.CreateContextFor(shop);
        Assert.Equal(2, await db2.Payments.CountAsync());
    }

    [Fact]
    public async Task A_deposit_on_a_booked_job_is_allowed_and_only_the_latest_payment_can_be_voided()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Void");
        var id = await SeedJobAsync(shop, JobStatus.Booked);
        var client = factory.CreateClientFor(user, shop);
        await PayAsync(client, id, "40");
        await PayAsync(client, id, "10");

        List<int> ids;
        using (var db = factory.CreateContextFor(shop))
        {
            ids = await db.Payments.OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        }

        await VoidAsync(client, id, ids[0]);
        using (var db = factory.CreateContextFor(shop)) Assert.Equal(2, await db.Payments.CountAsync());

        await VoidAsync(client, id, ids[1]);
        using (var db = factory.CreateContextFor(shop)) Assert.Equal(40m, (await db.Payments.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Marking_paid_by_hand_records_the_remaining_balance_as_cash()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Manual");
        var id = await SeedJobAsync(shop);
        var client = factory.CreateClientFor(user, shop);
        await PayAsync(client, id, "30");

        var response = await TestData.SubmitFormAsync(client, $"/Jobs/Details/{id}?handler=Status&to=Paid");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).SingleAsync();
        Assert.Equal(JobStatus.Paid, job.Status);
        Assert.Equal(job.Total, job.PaidTotal);
    }

    [Fact]
    public async Task Receivables_lists_owing_customers_with_a_whatsapp_link_and_totals_agree()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Receivables");
        var id = await SeedJobAsync(shop, JobStatus.Done, plate: "REC 1");
        await SeedJobAsync(shop, JobStatus.Paid, plate: "REC 2");
        var client = factory.CreateClientFor(user, shop);
        await PayAsync(client, id, "50");

        var page = await TestData.BodyAsync(await client.GetAsync("/Receivables"));
        Assert.Contains("Owes Money", page);
        Assert.Contains("100.01", page);
        Assert.Contains("https://wa.me/201000000001?text=", page);
        var home = (await TestData.BodyAsync(await client.GetAsync("/"))).Replace("\n", " ");
        Assert.Matches(@"data-outstanding[^>]*>.*?100\.01", home);

        using var db = factory.CreateContextFor(shop);
        var owed = await OutstandingBalances.LoadAsync(db);
        Assert.Single(owed);
        Assert.Equal(100.01m, owed[0].Owed);
    }

    [Fact]
    public async Task Another_shop_sees_no_receivables_and_cannot_pay_our_job()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "PayA");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "PayB");
        var id = await SeedJobAsync(shopA);
        await PayAsync(factory.CreateClientFor(userA, shopA), id, "10");
        var clientB = factory.CreateClientFor(userB, shopB);

        Assert.DoesNotContain("Owes Money", await TestData.BodyAsync(await clientB.GetAsync("/Receivables")));
        var token = await TestData.TokenAsync(clientB, "/Receivables");
        var response = await clientB.PostAsync($"/Jobs/Details/{id}?handler=AddPayment", new FormUrlEncodedContent(
            [new("NewPayment.Amount", "5"), new("NewPayment.Method", "Cash"), new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var dbB = factory.CreateContextFor(shopB);
        Assert.Empty(await dbB.Payments.ToListAsync());
        using var dbA = factory.CreateContextFor(shopA);
        Assert.Single(await dbA.Payments.ToListAsync());
    }

    [Fact]
    public async Task Invoice_shows_balance_due_and_partial_chip_and_backfill_pays_old_paid_jobs()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "InvoiceBalance");
        var id = await SeedJobAsync(shop);
        var legacy = await SeedJobAsync(shop, JobStatus.Paid, price: 100m, plate: "OLD 1");
        var client = factory.CreateClientFor(user, shop);
        await PayAsync(client, id, "50");

        var invoice = (await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{id}"))).Replace("\n", " ");
        Assert.Matches(@"data-invoice-balance[^>]*>.*?100\.01", invoice);
        Assert.Contains("Partly paid", invoice);

        using var db = factory.CreateContextFor(shop);
        await db.Database.ExecuteSqlRawAsync(PaymentBackfill.PaidJobsSql);
        var job = await db.JobCards.Include(j => j.Lines).Include(j => j.Payments).SingleAsync(j => j.Id == legacy);
        Assert.Equal(job.Total, job.PaidTotal);
    }
}
