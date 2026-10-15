using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>The owner's side: Send on WhatsApp, the link manager, revoke, the message template and the "ready" toast.</summary>
public class ShareLinkOwnerTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private sealed record Shop1(string UserId, int ShopId, int JobId, int EstimateId, HttpClient Client);

    private async Task<Shop1> SeedAsync(string shopName, string customer = "Mona Hassan", string? phone = "+201000000009", JobStatus status = JobStatus.Done)
    {
        var (user, shopId) = await TestData.CreateOwnerAsync(factory, shopName);
        using var db = factory.CreateContextFor(shopId);
        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = customer, Phone = phone ?? "" },
            Plate = "XYZ 789", PlateKey = "XYZ789", Make = "Kia", Model = "Rio", Year = 2020, Mileage = 40_000,
        };
        var job = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 10, 1), MileageAtVisit = 40_000, Status = status, InvoiceNumber = status >= JobStatus.Done ? 1 : null };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Filter", Quantity = 1, UnitPrice = 100 });
        var estimate = new Estimate { Vehicle = vehicle, Number = 1, CreatedOn = new DateOnly(2026, 10, 1) };
        estimate.Lines.Add(new EstimateLine { Type = JobLineType.Labor, Description = "Check", Quantity = 1, UnitPrice = 50 });
        db.JobCards.Add(job);
        db.Estimates.Add(estimate);
        await db.SaveChangesAsync();
        return new Shop1(user, shopId, job.Id, estimate.Id, factory.CreateClientFor(user, shopId));
    }

    private static string DecodedMessage(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.ToString();
        var text = location[(location.IndexOf("?text=", StringComparison.Ordinal) + 6)..];
        return Uri.UnescapeDataString(text);
    }

    [Fact]
    public async Task Only_the_hash_of_the_token_is_stored_not_the_token()
    {
        var s = await SeedAsync("HashAtRest");

        var response = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var token = Regex.Match(DecodedMessage(response), "/s/([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(ShareTokens.TextLength, token.Length);

        using var db = factory.CreateContextFor(s.ShopId);
        var link = await db.ShareLinks.SingleAsync();
        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(token)), link.TokenHash);
        Assert.DoesNotContain(token, link.TokenProtected ?? "");
        Assert.NotEqual(token, Convert.ToBase64String(link.TokenHash));
        Assert.True(link.ExpiresAtUtc > DateTime.UtcNow.AddDays(29) && link.ExpiresAtUtc < DateTime.UtcNow.AddDays(31));
        Assert.Equal(s.ShopId, link.ShopId);
    }

    [Fact]
    public async Task Send_on_whatsapp_opens_wa_me_with_the_customers_number_and_the_link_and_is_idempotent()
    {
        var s = await SeedAsync("SendShop");

        var first = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        var second = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.StartsWith("https://wa.me/201000000009?text=", first.Headers.Location!.ToString());
        var message = DecodedMessage(first);
        Assert.Contains("Mona Hassan", message);
        Assert.Contains("XYZ 789", message);
        Assert.Contains("is ready", message); // the job is Done
        Assert.Matches("http://localhost/s/[A-Za-z0-9_-]{43}", message);

        // The same live link again, not a second one.
        Assert.Equal(message, DecodedMessage(second));
        using var db = factory.CreateContextFor(s.ShopId);
        Assert.Equal(1, await db.ShareLinks.CountAsync());

        // An invoice link for the same job is a different link.
        var invoice = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Invoice");
        Assert.Contains("invoice", DecodedMessage(invoice));
        Assert.Equal(2, await db.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task The_message_for_a_job_still_in_progress_does_not_say_it_is_ready()
    {
        var s = await SeedAsync("InProgress", status: JobStatus.InProgress);

        var response = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.DoesNotContain("is ready", DecodedMessage(response));
        Assert.Contains("follow the status", DecodedMessage(response));
    }

    [Fact]
    public async Task Invoice_and_estimate_pages_have_their_own_send_buttons()
    {
        var s = await SeedAsync("PageButtons");

        var invoicePage = await TestData.BodyAsync(await s.Client.GetAsync($"/Jobs/Invoice/{s.JobId}"));
        Assert.Contains("data-invoice-whatsapp", invoicePage);
        var invoice = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Invoice/{s.JobId}?handler=Share");
        Assert.Equal(HttpStatusCode.Redirect, invoice.StatusCode);
        Assert.StartsWith("https://wa.me/201000000009", invoice.Headers.Location!.ToString());
        Assert.Contains("invoice", DecodedMessage(invoice));

        var estimatePage = await TestData.BodyAsync(await s.Client.GetAsync($"/Estimates/Details/{s.EstimateId}"));
        Assert.Contains("data-estimate-whatsapp", estimatePage);
        var estimate = await TestData.SubmitFormAsync(s.Client, $"/Estimates/Details/{s.EstimateId}?handler=Share");
        Assert.StartsWith("https://wa.me/201000000009", estimate.Headers.Location!.ToString());
        Assert.Contains("estimate", DecodedMessage(estimate));

        using var db = factory.CreateContextFor(s.ShopId);
        var kinds = await db.ShareLinks.Select(l => l.Kind).ToListAsync();
        Assert.Contains(ShareLinkKind.Invoice, kinds);
        Assert.Contains(ShareLinkKind.Estimate, kinds);
    }

    [Fact]
    public async Task A_customer_without_a_usable_phone_gets_a_clear_message_and_no_whatsapp_redirect()
    {
        var s = await SeedAsync("NoPhone", phone: "");

        var response = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("wa.me", response.Headers.Location!.ToString());
        var page = await TestData.BodyAsync(await s.Client.GetAsync(response.Headers.Location));
        Assert.Contains("no phone number WhatsApp can use", page);
        // The link can still be copied and sent by hand.
        Assert.Contains("data-copy=\"http://localhost/s/", page);
    }

    [Fact]
    public async Task The_shops_own_message_is_used_in_the_right_language_and_substitution_is_safe()
    {
        var s = await SeedAsync("CustomText", customer: "{link} {plate}");

        var save = await TestData.SubmitFormAsync(s.Client, "/ShopSettings?handler=ShareMessage",
            ("shareMessageEn", "Salam {name}! {shop}: car {plate} -> {link} {unknown} {{name}}"),
            ("shareMessageAr", "أهلاً {name}، {link}"));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var english = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        var message = DecodedMessage(english);
        // Inserted values are plain text and never scanned again: the customer's name "{link} {plate}" stays as typed.
        Assert.StartsWith("Salam {link} {plate}! CustomText: car XYZ 789 -> http://localhost/s/", message);
        Assert.Contains("{unknown} {", message);

        using var arabicClient = factory.CreateClientFor(s.UserId, s.ShopId);
        arabicClient.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Den%7Cuic%3Dar");
        var arabic = await TestData.SubmitFormAsync(arabicClient, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        Assert.StartsWith("أهلاً {link} {plate}، http://localhost/s/", DecodedMessage(arabic));
    }

    [Fact]
    public async Task Without_a_custom_message_the_arabic_default_is_used_in_arabic()
    {
        var s = await SeedAsync("ArabicDefault");
        using var arabicClient = factory.CreateClientFor(s.UserId, s.ShopId);
        arabicClient.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Den%7Cuic%3Dar");

        var response = await TestData.SubmitFormAsync(arabicClient, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.Contains("جاهزة", DecodedMessage(response));
        // The link remembers the language it was sent in.
        using var db = factory.CreateContextFor(s.ShopId);
        Assert.Equal("ar", (await db.ShareLinks.SingleAsync()).Language);
    }

    [Theory]
    [InlineData("Hello without the link")]
    public async Task A_message_without_the_link_placeholder_is_rejected_and_not_saved(string text)
    {
        var s = await SeedAsync("BadTemplate");

        var response = await TestData.SubmitFormAsync(s.Client, "/ShopSettings?handler=ShareMessage", ("shareMessageEn", text), ("shareMessageAr", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-share-message-error", await response.Content.ReadAsStringAsync());
        using var db = factory.CreateContextFor(s.ShopId);
        Assert.Null((await db.Shops.SingleAsync()).ShareMessageEn);
    }

    [Fact]
    public async Task A_message_longer_than_500_characters_is_rejected()
    {
        var s = await SeedAsync("LongTemplate");

        var response = await TestData.SubmitFormAsync(s.Client, "/ShopSettings?handler=ShareMessage",
            ("shareMessageEn", "{link} " + new string('x', 500)), ("shareMessageAr", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = factory.CreateContextFor(s.ShopId);
        Assert.Null((await db.Shops.SingleAsync()).ShareMessageEn);
    }

    [Fact]
    public async Task Clearing_the_message_goes_back_to_the_built_in_wording()
    {
        var s = await SeedAsync("ClearTemplate");
        await TestData.SubmitFormAsync(s.Client, "/ShopSettings?handler=ShareMessage", ("shareMessageEn", "Custom {link}"), ("shareMessageAr", ""));
        await TestData.SubmitFormAsync(s.Client, "/ShopSettings?handler=ShareMessage", ("shareMessageEn", "  "), ("shareMessageAr", ""));

        var response = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.Contains("is ready", DecodedMessage(response));
    }

    [Fact]
    public async Task The_link_manager_lists_live_links_with_views_and_revoking_works_immediately()
    {
        var s = await SeedAsync("Manager");
        var send = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        var url = Regex.Match(DecodedMessage(send), "http://localhost/s/[A-Za-z0-9_-]{43}").Value;
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await anonymous.GetAsync(url);
        await anonymous.GetAsync(url);

        var page = await TestData.BodyAsync(await s.Client.GetAsync($"/Jobs/Details/{s.JobId}"));
        Assert.Contains($"data-copy=\"{url}\"", page);
        Assert.Matches(@"data-view-count[^>]*>2<", page);

        int linkId;
        using (var db = factory.CreateContextFor(s.ShopId)) linkId = (await db.ShareLinks.SingleAsync()).Id;
        var revoke = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=RevokeLink&linkId={linkId}");
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);

        // At once: the customer's link is dead, and the manager no longer lists it.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        var after = await TestData.BodyAsync(await s.Client.GetAsync($"/Jobs/Details/{s.JobId}"));
        Assert.Contains("data-share-empty", after);
        Assert.DoesNotContain(url, after);

        // Sending again makes a brand new link; the revoked one is never brought back.
        var again = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        var newUrl = Regex.Match(DecodedMessage(again), "http://localhost/s/[A-Za-z0-9_-]{43}").Value;
        Assert.NotEqual(url, newUrl);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(newUrl)).StatusCode);
    }

    [Fact]
    public async Task An_expired_link_is_not_reused()
    {
        var s = await SeedAsync("ExpiredReuse");
        var first = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");
        using (var db = factory.CreateContextFor(s.ShopId))
        {
            (await db.ShareLinks.SingleAsync()).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var second = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Share&kind=Status");

        Assert.NotEqual(DecodedMessage(first), DecodedMessage(second));
        using var check = factory.CreateContextFor(s.ShopId);
        Assert.Equal(2, await check.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task Another_shop_cannot_send_revoke_or_list_our_links()
    {
        var a = await SeedAsync("OwnerA");
        var b = await SeedAsync("OwnerB");
        await TestData.SubmitFormAsync(a.Client, $"/Jobs/Details/{a.JobId}?handler=Share&kind=Status");
        int linkId;
        using (var db = factory.CreateContextFor(a.ShopId)) linkId = (await db.ShareLinks.SingleAsync()).Id;

        // B tries A's job and estimate: 404, and no link is created for B.
        var token = await TestData.TokenAsync(b.Client, "/Jobs");
        var content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/Jobs/Details/{a.JobId}?handler=Share&kind=Status", content)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/Jobs/Details/{a.JobId}?handler=CreateLink&kind=Invoice", content)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/Estimates/Details/{a.EstimateId}?handler=Share", content)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/Jobs/Details/{b.JobId}?handler=RevokeLink&linkId={linkId}", content)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/Jobs/Details/{a.JobId}?handler=RevokeLink&linkId={linkId}", content)).StatusCode);

        using var dbB = factory.CreateContextFor(b.ShopId);
        Assert.Empty(await dbB.ShareLinks.ToListAsync());
        using var dbA = factory.CreateContextFor(a.ShopId);
        Assert.Null((await dbA.ShareLinks.SingleAsync()).RevokedAtUtc);
    }

    [Fact]
    public async Task Marking_a_job_done_offers_the_whatsapp_toast_and_other_moves_do_not()
    {
        var s = await SeedAsync("ReadyToast", status: JobStatus.InProgress);

        var done = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Status&to=Done");
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        var page = await TestData.BodyAsync(await s.Client.GetAsync(done.Headers.Location));
        Assert.Contains("data-toast-action=\"#ready-share-form\"", page);
        Assert.Contains("Tell the customer on WhatsApp", page);
        Assert.Contains("id=\"ready-share-form\"", page);

        // It is a one-time offer: reloading does not show it again, and the panel button is always there.
        var reload = await TestData.BodyAsync(await s.Client.GetAsync($"/Jobs/Details/{s.JobId}"));
        Assert.DoesNotContain("data-toast-action", reload);
        Assert.Contains("data-share-whatsapp=\"status\"", reload);

        // Nothing is sent by the server: marking it done made no link at all.
        using var db = factory.CreateContextFor(s.ShopId);
        Assert.Empty(await db.ShareLinks.ToListAsync());
    }

    [Fact]
    public async Task No_whatsapp_toast_when_a_job_is_only_started()
    {
        var s = await SeedAsync("NoToast", status: JobStatus.Booked);

        var started = await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Status&to=InProgress");
        var page = await TestData.BodyAsync(await s.Client.GetAsync(started.Headers.Location));

        Assert.DoesNotContain("data-toast-action", page);
    }

    [Fact]
    public async Task The_last_updated_date_follows_the_status_change()
    {
        var s = await SeedAsync("LastUpdated", status: JobStatus.InProgress);
        await TestData.SubmitFormAsync(s.Client, $"/Jobs/Details/{s.JobId}?handler=Status&to=Done");

        using var db = factory.CreateContextFor(s.ShopId);
        var job = await db.JobCards.SingleAsync();
        Assert.NotNull(job.StatusChangedAtUtc);
        Assert.True(job.StatusChangedAtUtc > DateTime.UtcNow.AddMinutes(-2));
    }
}
