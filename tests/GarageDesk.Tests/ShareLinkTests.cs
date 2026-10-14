using System.Net;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>The customer's public pages (/s/{token}): the token rules, what is shown and what is not.</summary>
public class ShareLinkTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private const string JobNote = "INTERNAL-JOB-NOTE-do-not-show";
    private const string EstimateNote = "INTERNAL-ESTIMATE-NOTE-do-not-show";
    private const string CustomerNote = "CUSTOMER-NOTE-do-not-show";

    private sealed record Seeded(int ShopId, string OwnerId, int JobId, int EstimateId, int VehicleId);

    private HttpClient Anonymous() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<Seeded> SeedAsync(string shopName, string plate = "ABC 123", string customer = "Mona Hassan")
    {
        var (owner, shopId) = await TestData.CreateOwnerAsync(factory, shopName);
        using var db = factory.CreateContextFor(shopId);

        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = customer, Phone = "+201000000009", Notes = CustomerNote },
            Plate = plate, PlateKey = plate.Replace(" ", ""), Make = "Skoda", Model = "Octavia", Year = 2018, Mileage = 98_000,
        };
        var job = new JobCard
        {
            Vehicle = vehicle, DateIn = new DateOnly(2026, 10, 1), MileageAtVisit = 98_000,
            Status = JobStatus.Done, InvoiceNumber = 7, Notes = JobNote, DiscountPercent = 10,
        };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Brake pads", Quantity = 2, UnitPrice = 150 });
        job.Lines.Add(new JobLine { Type = JobLineType.Labor, Description = "Fitting", Quantity = 1, UnitPrice = 200 });
        job.Payments.Add(new Payment { Amount = 100, Method = PaymentMethod.Cash, PaidOn = new DateOnly(2026, 10, 1), Note = "PAYMENT-NOTE-do-not-show" });

        var estimate = new Estimate
        {
            Vehicle = vehicle, Number = 3, CreatedOn = new DateOnly(2026, 9, 30), ValidUntil = new DateOnly(2026, 12, 31), Notes = EstimateNote,
        };
        estimate.Lines.Add(new EstimateLine { Type = JobLineType.Part, Description = "Oil filter", Quantity = 1, UnitPrice = 80 });

        db.JobCards.Add(job);
        db.Estimates.Add(estimate);
        await db.SaveChangesAsync();
        return new Seeded(shopId, owner, job.Id, estimate.Id, vehicle.Id);
    }

    /// <summary>Stores a link the way the service does and returns the secret token (the only time it is known).</summary>
    private async Task<string> LinkAsync(int shopId, ShareLinkKind kind, int targetId, Action<ShareLink>? tweak = null)
    {
        var token = ShareTokens.Generate();
        var link = new ShareLink
        {
            Kind = kind, TargetId = targetId, TokenHash = ShareTokens.Hash(token), Language = "en",
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        };
        tweak?.Invoke(link);
        using var db = factory.CreateContextFor(shopId);
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        return token;
    }

    [Fact]
    public void Tokens_have_at_least_128_bits_are_url_safe_and_never_repeat()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => ShareTokens.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        foreach (var token in tokens)
        {
            Assert.Equal(ShareTokens.TextLength, token.Length);
            Assert.True(ShareTokens.IsWellFormed(token));
            Assert.Matches("^[A-Za-z0-9_-]+$", token);
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            Assert.True(bytes.Length * 8 >= 128);
            Assert.Equal(256, bytes.Length * 8);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789012!")]
    [InlineData("01234567890123456789012345678901234567890123")]
    public void Malformed_tokens_are_recognised_without_touching_the_database(string? token) =>
        Assert.False(ShareTokens.IsWellFormed(token));

    [Fact]
    public async Task Unknown_expired_revoked_and_malformed_links_give_the_same_404()
    {
        var seed = await SeedAsync("Identical404");
        var expired = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId, l => l.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1));
        var revoked = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId, l => l.RevokedAtUtc = DateTime.UtcNow);
        var live = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId);
        var client = Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/s/{live}")).StatusCode);

        var responses = new List<HttpResponseMessage>();
        foreach (var path in new[] { ShareTokens.Generate(), expired, revoked, "not-a-token", new string('A', 43), live[..42] })
        {
            responses.Add(await client.GetAsync($"/s/{path}"));
        }

        var first = responses[0];
        var firstBody = await first.Content.ReadAsStringAsync();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(firstBody, await response.Content.ReadAsStringAsync());
            Assert.Equal(first.Content.Headers.ContentType?.ToString(), response.Content.Headers.ContentType?.ToString());
            Assert.Equal(first.Content.Headers.ContentLength, response.Content.Headers.ContentLength);
            Assert.Equal(first.Headers.CacheControl?.ToString(), response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task A_revoked_link_stops_working_immediately()
    {
        var seed = await SeedAsync("RevokeNow");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Status, seed.JobId);
        var client = Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/s/{token}")).StatusCode);

        using (var db = factory.CreateContextFor(seed.ShopId))
        {
            (await db.ShareLinks.SingleAsync()).RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/s/{token}")).StatusCode);
    }

    [Fact]
    public async Task The_invoice_page_shows_the_invoice_and_nothing_internal()
    {
        var seed = await SeedAsync("InvoiceShop");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId);

        var response = await Anonymous().GetAsync($"/s/{token}");
        var html = await TestData.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("INV-0007", html);
        Assert.Contains("InvoiceShop", html);
        Assert.Contains("Mona Hassan", html);
        Assert.Contains("ABC 123", html);
        Assert.Contains("Brake pads", html);
        // 2 x 150 + 200 = 500, less 10% = 450, less 100 received = 350.
        Assert.Matches(@"data-share-total[^>]*>.*?450\.00", html.Replace("\n", " "));
        Assert.Matches(@"data-share-balance[^>]*>.*?350\.00", html.Replace("\n", " "));

        // Nothing internal, no links into the app, no ids, no staff.
        foreach (var secret in new[] { JobNote, EstimateNote, CustomerNote, "PAYMENT-NOTE", "owner@example.com", "+201000000009" })
        {
            Assert.DoesNotContain(secret, html);
        }
        Assert.DoesNotContain("/Jobs/", html);
        Assert.DoesNotContain("/Customers/", html);
        Assert.DoesNotContain("/Vehicles/", html);
        Assert.DoesNotContain("/Identity/", html);
        Assert.DoesNotContain($"value=\"{seed.JobId}\"", html);
    }

    [Fact]
    public async Task The_estimate_page_shows_the_estimate_and_not_its_notes()
    {
        var seed = await SeedAsync("EstimateShop");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Estimate, seed.EstimateId);

        var html = await TestData.BodyAsync(await Anonymous().GetAsync($"/s/{token}"));

        Assert.Contains("EST-0003", html);
        Assert.Contains("Oil filter", html);
        Assert.Contains("2026-12-31", html);
        Assert.DoesNotContain(EstimateNote, html);
        Assert.DoesNotContain(JobNote, html);
        Assert.DoesNotContain("Brake pads", html);
        Assert.DoesNotContain("data-share-balance", html);
    }

    [Fact]
    public async Task The_status_page_shows_the_car_the_stepper_and_the_balance_but_no_prices_or_lines()
    {
        var seed = await SeedAsync("StatusShop");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Status, seed.JobId);

        var html = await TestData.BodyAsync(await Anonymous().GetAsync($"/s/{token}"));

        Assert.Contains("StatusShop", html);
        Assert.Contains("ABC 123", html);
        Assert.Contains("Skoda Octavia", html);
        Assert.Contains("Booked", html);
        Assert.Contains("Ready", html);
        Assert.Matches(@"aria-current=""step""", html);
        Assert.Matches(@"data-share-balance[^>]*>.*?350\.00", html.Replace("\n", " "));
        Assert.DoesNotContain("Brake pads", html);
        Assert.DoesNotContain("Fitting", html);
        Assert.DoesNotContain(JobNote, html);
        Assert.DoesNotContain("Mona Hassan", html);
    }

    [Fact]
    public async Task A_link_can_never_show_another_shops_data_even_if_its_target_id_is_tampered()
    {
        var a = await SeedAsync("ShopA-Tamper", plate: "AAA 111", customer: "Customer Of A");
        var b = await SeedAsync("ShopB-Tamper", plate: "BBB 222", customer: "Customer Of B");

        // Shop A's link, but pointing at shop B's job card and estimate (as if someone edited the row).
        var invoice = await LinkAsync(a.ShopId, ShareLinkKind.Invoice, b.JobId);
        var status = await LinkAsync(a.ShopId, ShareLinkKind.Status, b.JobId);
        var estimate = await LinkAsync(a.ShopId, ShareLinkKind.Estimate, b.EstimateId);
        var client = Anonymous();

        foreach (var token in new[] { invoice, status, estimate })
        {
            var response = await client.GetAsync($"/s/{token}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Customer Of B", html);
            Assert.DoesNotContain("BBB 222", html);
        }

        // The honest links still show their own shop's data.
        var own = await LinkAsync(a.ShopId, ShareLinkKind.Invoice, a.JobId);
        var ownHtml = await TestData.BodyAsync(await client.GetAsync($"/s/{own}"));
        Assert.Contains("Customer Of A", ownHtml);
        Assert.DoesNotContain("Customer Of B", ownHtml);
    }

    [Fact]
    public async Task The_signed_in_shop_of_the_visitor_does_not_change_what_a_link_shows()
    {
        var a = await SeedAsync("OwnerOfLink", customer: "Customer Of A");
        var b = await SeedAsync("OtherSignedIn", customer: "Customer Of B");
        var token = await LinkAsync(a.ShopId, ShareLinkKind.Invoice, a.JobId);

        var asOtherShop = factory.CreateClientFor(b.OwnerId, b.ShopId);
        var html = await TestData.BodyAsync(await asOtherShop.GetAsync($"/s/{token}"));

        Assert.Contains("Customer Of A", html);
        Assert.DoesNotContain("Customer Of B", html);
    }

    [Fact]
    public async Task The_public_path_has_no_route_that_takes_a_shop_or_object_id()
    {
        var seed = await SeedAsync("NoIds");
        var client = Anonymous();

        // Guessing ids in the URL finds nothing: the token is the only way in.
        foreach (var path in new[] { $"/s/{seed.JobId}", $"/s/{seed.ShopId}/{seed.JobId}", $"/s/invoice/{seed.JobId}", $"/s?shopId={seed.ShopId}&id={seed.JobId}", $"/Share/Open/{seed.JobId}" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task Public_responses_are_private_no_store_noindex_and_send_no_referrer()
    {
        var seed = await SeedAsync("Headers");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId);
        var client = Anonymous();

        foreach (var path in new[] { $"/s/{token}", $"/s/{ShareTokens.Generate()}" })
        {
            var response = await client.GetAsync(path);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
            Assert.Contains("private", response.Headers.CacheControl?.ToString());
            Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        }

        var html = await client.GetStringAsync($"/s/{token}");
        Assert.Contains("name=\"robots\" content=\"noindex", html);
        Assert.Contains("name=\"referrer\" content=\"no-referrer\"", html);
    }

    [Fact]
    public async Task A_view_is_counted_and_the_owner_previewing_it_is_not()
    {
        var seed = await SeedAsync("ViewCount");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Status, seed.JobId);
        var client = Anonymous();

        await client.GetAsync($"/s/{token}");
        await client.GetAsync($"/s/{token}");
        await factory.CreateClientFor(seed.OwnerId, seed.ShopId).GetAsync($"/s/{token}");
        await client.GetAsync($"/s/{token}?handler=logo"); // the logo is not a view

        using var db = factory.CreateContextFor(seed.ShopId);
        var link = await db.ShareLinks.SingleAsync();
        Assert.Equal(2, link.ViewCount);
        Assert.NotNull(link.LastViewedAtUtc);
    }

    [Fact]
    public async Task Chat_app_link_previews_are_not_counted_as_views()
    {
        var seed = await SeedAsync("PreviewBots");
        var token = await LinkAsync(seed.ShopId, ShareLinkKind.Status, seed.JobId);
        var client = Anonymous();

        var preview = new HttpRequestMessage(HttpMethod.Get, $"/s/{token}");
        preview.Headers.Add("User-Agent", "WhatsApp/2.23.20.0 A");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(preview)).StatusCode); // it still gets the page
        var human = new HttpRequestMessage(HttpMethod.Get, $"/s/{token}");
        human.Headers.Add("User-Agent", "Mozilla/5.0 (Linux; Android 14) Chrome/120 Mobile Safari/537.36");
        await client.SendAsync(human);

        using var db = factory.CreateContextFor(seed.ShopId);
        Assert.Equal(1, (await db.ShareLinks.SingleAsync()).ViewCount);
    }

    [Fact]
    public async Task The_page_follows_the_links_language_and_the_visitors_own_choice_and_is_right_to_left_in_arabic()
    {
        var seed = await SeedAsync("Languages");
        var arabicLink = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId, l => l.Language = "ar");
        var client = Anonymous();

        var fromLink = await TestData.BodyAsync(await client.GetAsync($"/s/{arabicLink}"));
        Assert.Contains("dir=\"rtl\"", fromLink);
        Assert.Contains("فاتورة", fromLink);

        // The visitor's own choice (the language switch sets this cookie) wins over the link's language.
        var request = new HttpRequestMessage(HttpMethod.Get, $"/s/{arabicLink}");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c%3Den%7Cuic%3Den");
        var english = await TestData.BodyAsync(await client.SendAsync(request));
        Assert.Contains("dir=\"ltr\"", english);
        Assert.Contains("Brake pads", english);

        // The page has the language switch.
        Assert.Contains("lang-switch", english);
    }

    [Fact]
    public async Task The_logo_is_served_only_through_a_live_link()
    {
        var seed = await SeedAsync("Logo");
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        using (var db = factory.CreateContextFor(seed.ShopId))
        {
            db.ShopLogos.Add(new ShopLogo { Bytes = png, ContentType = "image/png", UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var live = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId);
        var revoked = await LinkAsync(seed.ShopId, ShareLinkKind.Invoice, seed.JobId, l => l.RevokedAtUtc = DateTime.UtcNow);
        var client = Anonymous();

        var ok = await client.GetAsync($"/s/{live}?handler=logo");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/png", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/s/{revoked}?handler=logo")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/shop/logo")).StatusCode); // the signed-in logo endpoint stays closed
    }

    [Fact]
    public async Task Too_many_requests_from_one_visitor_get_429()
    {
        using var limited = new LimitedFactory();
        var (_, shopId) = await TestData.CreateOwnerAsync(limited, "RateLimited");
        using (var db = limited.CreateContextFor(shopId))
        {
            var vehicle = new Vehicle { Customer = new Customer { Name = "C", Phone = "+201000000009" }, Plate = "RL 1", PlateKey = "RL1", Make = "Kia", Model = "Rio", Year = 2020 };
            db.JobCards.Add(new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 10, 1) });
            await db.SaveChangesAsync();
        }
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.GetAsync($"/s/{ShareTokens.Generate()}")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
        var rejected = await client.GetAsync($"/s/{ShareTokens.Generate()}");
        Assert.Contains("no-store", rejected.Headers.CacheControl?.ToString());
        Assert.Equal("noindex, nofollow, noarchive", rejected.Headers.GetValues("X-Robots-Tag").Single());
    }

    private sealed class LimitedFactory : GarageDeskWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ShareLinks:PermitPerMinute", "3");
        }
    }
}
