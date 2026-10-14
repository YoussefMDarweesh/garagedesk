using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>v1.3: the job card history. Who created it, changed its status, took a payment or assigned it.</summary>
public class AuditTrailTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private async Task<List<JobAuditEntry>> EntriesAsync(int shop, int job)
    {
        using var db = factory.CreateContextFor(shop);
        return await db.JobAuditEntries.AsNoTracking().Where(a => a.JobCardId == job).OrderBy(a => a.Id).ToListAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string url, params (string, string)[] fields)
    {
        var token = await TestData.TokenAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields
            .Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))
            .Append(new("__RequestVerificationToken", token))));
    }

    [Fact]
    public async Task Creating_a_job_records_who_created_it_and_who_it_was_given_to()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Audit Create");
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Tina Tech");
        var seeded = await TestData.SeedJobAsync(factory, shop, plate: "AUD 1");
        using var db0 = factory.CreateContextFor(shop);
        var vehicle = (await db0.JobCards.SingleAsync(j => j.Id == seeded)).VehicleId;
        var client = factory.CreateClientFor(owner, shop);

        var response = await TestData.SubmitFormAsync(client, $"/Jobs/Create?vehicleId={vehicle}",
            ("Input.VehicleId", vehicle.ToString()), ("Input.DateIn", "2026-04-01"), ("Input.MileageAtVisit", "5000"), ("Input.AssignedToUserId", tech));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var db = factory.CreateContextFor(shop);
        var job = await db.JobCards.OrderByDescending(j => j.Id).FirstAsync();
        var entries = await EntriesAsync(shop, job.Id);

        Assert.Equal([JobAuditKind.Created, JobAuditKind.Assigned], entries.Select(e => e.Kind).ToArray());
        Assert.All(entries, e => Assert.Equal(owner, e.UserId));
        Assert.All(entries, e => Assert.Equal(shop, e.ShopId));
        Assert.Equal("Tina Tech", entries[1].Detail);
        Assert.True(entries[0].AtUtc > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Status_changes_payments_and_voids_are_recorded_with_the_person_who_did_them()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Audit Money");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier, fullName: "Cathy Cashier");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done); // 123.45 + 80 = 203.45
        var client = factory.CreateClientFor(cashier, shop);

        await PostAsync(client, $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=AddPayment",
            ("NewPayment.Amount", "50"), ("NewPayment.Method", "Card"), ("NewPayment.Note", ""));
        await PostAsync(client, $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=Status&to=InProgress");

        using (var db = factory.CreateContextFor(shop))
        {
            var paymentId = (await db.Payments.SingleAsync()).Id;
            await PostAsync(client, $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=VoidPayment&paymentId={paymentId}");
        }

        var entries = await EntriesAsync(shop, job);
        Assert.Equal(
            [JobAuditKind.PaymentAdded, JobAuditKind.StatusChanged, JobAuditKind.PaymentVoided],
            entries.Select(e => e.Kind).ToArray());
        Assert.Equal("50.00|Card", entries[0].Detail);
        Assert.Equal("Done>InProgress", entries[1].Detail);
        Assert.Equal("50.00|Card", entries[2].Detail);
        Assert.All(entries, e =>
        {
            Assert.Equal(cashier, e.UserId);
            Assert.Equal("Cathy Cashier", e.UserName);
        });
    }

    [Fact]
    public async Task A_technicians_status_change_is_recorded_under_their_name()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Audit Tech");
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Tom Tech");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked, tech);
        var client = factory.CreateClientFor(tech, shop);

        await PostAsync(client, $"/Work/Job/{job}", $"/Work/Job/{job}?handler=Status&to=InProgress");

        var entry = Assert.Single(await EntriesAsync(shop, job));
        Assert.Equal(JobAuditKind.StatusChanged, entry.Kind);
        Assert.Equal("Booked>InProgress", entry.Detail);
        Assert.Equal("Tom Tech", entry.UserName);
        Assert.Equal(tech, entry.UserId);
    }

    [Fact]
    public async Task Reassigning_a_job_is_recorded()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Audit Assign");
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician, fullName: "Ann Tech");
        var job = await TestData.SeedJobAsync(factory, shop, plate: "AUD 2");
        var client = factory.CreateClientFor(owner, shop);

        await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{job}",
            ("Input.DateIn", "2026-03-01"), ("Input.MileageAtVisit", "1000"), ("Input.AssignedToUserId", tech));
        await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{job}",
            ("Input.DateIn", "2026-03-01"), ("Input.MileageAtVisit", "1000"), ("Input.AssignedToUserId", tech)); // unchanged: no new entry
        await TestData.SubmitFormAsync(client, $"/Jobs/Edit/{job}",
            ("Input.DateIn", "2026-03-01"), ("Input.MileageAtVisit", "1000"), ("Input.AssignedToUserId", ""));

        var entries = await EntriesAsync(shop, job);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Ann Tech", entries[0].Detail);
        Assert.Equal("", entries[1].Detail);
    }

    [Fact]
    public async Task Only_the_owner_sees_the_history_panel()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Audit Panel");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked);
        await PostAsync(factory.CreateClientFor(owner, shop), $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=Status&to=InProgress");

        var ownerHtml = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync($"/Jobs/Details/{job}"));
        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync($"/Jobs/Details/{job}"));

        Assert.Contains("data-job-history", ownerHtml);
        Assert.Contains("Status changed from Booked to In progress", ownerHtml);
        Assert.DoesNotContain("data-job-history", cashierHtml);
        Assert.DoesNotContain("Status changed from", cashierHtml);
    }

    [Fact]
    public async Task The_history_cannot_be_edited_or_deleted_and_never_crosses_shops()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Audit Immutable");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Audit Other");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Booked);
        await PostAsync(factory.CreateClientFor(owner, shop), $"/Jobs/Details/{job}", $"/Jobs/Details/{job}?handler=Status&to=InProgress");

        using (var db = factory.CreateContextFor(shop))
        {
            var entry = await db.JobAuditEntries.FirstAsync();
            entry.Detail = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var db = factory.CreateContextFor(shop))
        {
            db.JobAuditEntries.Remove(await db.JobAuditEntries.FirstAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var db = factory.CreateContextFor(otherShop))
        {
            Assert.Empty(await db.JobAuditEntries.ToListAsync());
        }
    }
}
