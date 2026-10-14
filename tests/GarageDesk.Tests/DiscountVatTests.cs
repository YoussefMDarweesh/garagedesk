using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class DiscountVatTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Theory]
    // subtotal, discount amount, discount percent, vat percent, expected total
    [InlineData(1000.0, null, null, null, 1000.00)]          // unchanged without discount or VAT
    [InlineData(1000.0, 100.0, null, null, 900.00)]            // fixed discount
    [InlineData(1000.0, null, 10.0, null, 900.00)]             // percent discount
    [InlineData(1000.0, 50.0, 10.0, null, 900.00)]               // percent wins over the amount
    [InlineData(1000.0, null, null, 14.0, 1140.00)]            // VAT only
    [InlineData(1000.0, 100.0, null, 14.0, 1026.00)]             // discount first, then VAT: (1000-100) * 1.14
    [InlineData(99.99, null, 15.0, 14.0, 96.89)]             // 15.00 off, then 14% of 84.99 = 11.90
    [InlineData(100.0, 500.0, null, null, 0.00)]               // never below zero
    public void Total_is_subtotal_minus_discount_plus_vat_rounded_to_the_piastre(
        double subtotal, double? amount, double? percent, double? vat, double expected)
    {
        var total = JobTotals.Total((decimal)subtotal, (decimal?)amount, (decimal?)percent, (decimal?)vat);

        Assert.Equal((decimal)expected, total);
    }

    [Fact]
    public void Half_piastres_round_away_from_zero()
    {
        // 0.5% of 10.10 = 0.0505 -> 0.05 ; 0.5% of 10.30 = 0.0515 -> 0.05 ; 10 * 12.5% of 0.04 -> 0.005 -> 0.01
        Assert.Equal(0.01m, JobTotals.Discount(0.04m, null, 12.5m));
        Assert.Equal(0.01m, JobTotals.Vat(0.04m, 12.5m));
    }

    [Fact]
    public void A_job_card_without_discount_or_vat_totals_exactly_its_lines()
    {
        var job = new JobCard();
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Quantity = 3, UnitPrice = 33.335m });
        job.Lines.Add(new JobLine { Type = JobLineType.Labor, Quantity = 1.5m, UnitPrice = 200 });

        Assert.Equal(job.Subtotal, job.Total);
        Assert.Equal(100.01m + 300m, job.Total);
        Assert.Equal(0, job.DiscountValue);
        Assert.Equal(0, job.VatValue);
    }

    private async Task<int> SeedJobAsync(int shopId, decimal? percent = null, decimal? vat = null)
    {
        using var db = factory.CreateContextFor(shopId);
        var customer = new Customer { Name = "Vat Buyer", Phone = "+201000000002" };
        var vehicle = new Vehicle { Customer = customer, Plate = "VAT 1", PlateKey = "VAT1", Make = "Kia", Model = "Rio", Year = 2019, Mileage = 1 };
        var job = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 3, 1), MileageAtVisit = 1, Status = JobStatus.InProgress, DiscountPercent = percent };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Part", Quantity = 1, UnitPrice = 1000 });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Shop_vat_is_saved_snapshotted_at_issue_and_shown_on_job_card_and_invoice()
    {
        var (user, shop) = await TestData.CreateOwnerAsync(factory, "Vat Shop");
        var client = factory.CreateClientFor(user, shop);

        var saved = await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Vat", ("vatPercent", "14"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, saved.StatusCode);
        var id = await SeedJobAsync(shop, percent: 10);

        await TestData.SubmitFormAsync(client, $"/Jobs/Details/{id}?handler=Status&to=Done");

        // A later change of the shop setting does not alter the issued job card.
        await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Vat", ("vatPercent", "20"));

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.Include(j => j.Lines).SingleAsync();
        Assert.Equal(14m, job.VatPercent);
        Assert.Equal(1026m, job.Total);

        var details = (await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{id}"))).Replace("\n", " ");
        Assert.Matches(@"data-job-total[^>]*>.*?1,026\.00", details);
        Assert.Contains("data-job-vat", details);
        var invoice = (await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{id}"))).Replace("\n", " ");
        Assert.Matches(@"data-invoice-total[^>]*>.*?1,026\.00", invoice);
        Assert.Matches(@"data-invoice-vat[^>]*>.*?126\.00", invoice);
        Assert.Matches(@"data-invoice-discount[^>]*>.*?100\.00", invoice);
    }

    [Fact]
    public async Task Discount_form_rejects_both_kinds_and_other_shops_cannot_edit()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "DiscA");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "DiscB");
        var id = await SeedJobAsync(shopA);
        var clientA = factory.CreateClientFor(userA, shopA);

        await TestData.SubmitFormAsync(clientA, $"/Jobs/Edit/{id}",
            ("Input.DateIn", "2026-03-01"), ("Input.MileageAtVisit", "1"), ("Input.DiscountAmount", "50"), ("Input.DiscountPercent", "5"));
        using (var db = factory.CreateContextFor(shopA))
        {
            Assert.Null((await db.JobCards.SingleAsync()).DiscountAmount);
        }

        await TestData.SubmitFormAsync(clientA, $"/Jobs/Edit/{id}",
            ("Input.DateIn", "2026-03-01"), ("Input.MileageAtVisit", "1"), ("Input.DiscountAmount", "50"));
        using (var db = factory.CreateContextFor(shopA))
        {
            Assert.Equal(50m, (await db.JobCards.SingleAsync()).DiscountAmount);
        }

        var clientB = factory.CreateClientFor(userB, shopB);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await clientB.GetAsync($"/Jobs/Edit/{id}")).StatusCode);
    }
}
