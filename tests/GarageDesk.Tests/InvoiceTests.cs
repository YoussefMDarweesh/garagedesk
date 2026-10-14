using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class InvoiceTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static DateOnly Today => ShopClock.Today();

    // ---- what the invoice says ----

    [Fact]
    public async Task The_invoice_shows_the_shop_customer_vehicle_lines_and_total()
    {
        var (userId, shopId, jobId) = await SeedAsync("Nile Auto Care", status: JobStatus.Paid);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}"));
        var flat = Regex.Replace(html, @"\s+", " ");

        // shop
        Assert.Contains("Nile Auto Care", flat);
        Assert.Contains("12 Nile Corniche, Cairo", flat);
        Assert.Contains("+201001234567", flat);
        // number and date
        Assert.Contains($"#{jobId}", flat);
        Assert.Contains("2026-10-01", flat);
        // customer
        Assert.Contains("Ahmed Hassan", flat);
        Assert.Contains("+201012345678", flat);
        // vehicle
        Assert.Contains("Skoda Octavia", flat);
        Assert.Contains("ABC 1234", flat);
        Assert.Contains("120,350", flat);
        // lines: description, type, quantity, unit price, line total
        Assert.Contains("Brake pads", flat);
        Assert.Contains("Fit brake pads", flat);
        Assert.Matches(@"Brake pads.*?Part.*?2.*?150\.00.*?300\.00", flat);
        Assert.Matches(@"Fit brake pads.*?Labor.*?1\.5.*?200\.00.*?300\.00", flat);
        // total, in EGP
        Assert.Matches(@"data-invoice-total[^>]*>.*?600\.00.*?EGP", flat);
        Assert.Contains("Thank you for your business.", flat);
    }

    [Fact]
    public async Task The_invoice_total_always_equals_the_job_card_total()
    {
        var (userId, shopId, jobId) = await SeedAsync("Totals Garage", status: JobStatus.Done);
        var client = factory.CreateClientFor(userId, shopId);

        var invoice = Regex.Replace(await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}")), @"\s+", " ");
        var jobPage = Regex.Replace(await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}")), @"\s+", " ");

        var onInvoice = Regex.Match(invoice, @"data-invoice-total[^>]*>.*?<span class=""ltr"">([\d,\.]+)</span>").Groups[1].Value;
        var onJobCard = Regex.Match(jobPage, @"data-job-total[^>]*>.*?<span class=""ltr"">([\d,\.]+)</span>").Groups[1].Value;

        Assert.Equal("600.00", onInvoice);
        Assert.Equal(onJobCard, onInvoice);
    }

    [Theory]
    [InlineData(JobStatus.Paid, "Paid", "is-paid")]
    [InlineData(JobStatus.Done, "Unpaid", "is-unpaid")]
    [InlineData(JobStatus.InProgress, "Unpaid", "is-unpaid")]
    [InlineData(JobStatus.Booked, "Unpaid", "is-unpaid")]
    public async Task The_payment_status_is_paid_only_when_the_job_card_is_paid(JobStatus status, string label, string css)
    {
        var (userId, shopId, jobId) = await SeedAsync($"Status {status} Garage", status);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}"));

        Assert.Matches($@"inv-status {css}""[^>]*>\s*{label}\s*<", html);
    }

    [Fact]
    public async Task The_invoice_does_not_print_internal_notes()
    {
        var (userId, shopId, jobId) = await SeedAsync("Notes Garage", JobStatus.Paid, notes: "Customer was rude about the price");
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}"));

        Assert.DoesNotContain("rude about the price", html);
    }

    [Fact]
    public async Task The_job_card_page_links_to_the_invoice()
    {
        var (userId, shopId, jobId) = await SeedAsync("Link Garage", JobStatus.Booked);
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Details/{jobId}"));

        Assert.Contains($"href=\"/Jobs/Invoice/{jobId}\"", html);
    }

    // ---- the logo ----

    [Fact]
    public async Task The_logo_is_shown_only_when_the_shop_has_one_otherwise_the_name_is_the_wordmark()
    {
        var (userId, shopId, jobId) = await SeedAsync("Logo Invoice Garage", JobStatus.Paid);
        var without = factory.CreateClientFor(userId, shopId);
        var with = factory.CreateClientFor(userId, shopId);
        with.DefaultRequestHeaders.Add(TestAuthHandler.ShopLogoHeader, "777");

        var plain = await TestData.BodyAsync(await without.GetAsync($"/Jobs/Invoice/{jobId}"));
        var branded = await TestData.BodyAsync(await with.GetAsync($"/Jobs/Invoice/{jobId}"));

        Assert.DoesNotContain("inv-logo", plain);
        Assert.Contains("class=\"inv-wordmark\"", plain);
        Assert.Contains("class=\"inv-logo\" src=\"/shop/logo?v=777\"", branded);
        Assert.DoesNotContain("class=\"inv-wordmark\"", branded);
    }

    // ---- many lines ----

    [Fact]
    public async Task A_long_job_lists_every_line_and_the_total_still_adds_up()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Long Garage");
        int jobId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var vehicle = new Vehicle
            {
                Customer = new Customer { Name = "Long Customer", Phone = "+201000000000" },
                Plate = "LNG 1", PlateKey = "LNG1", Make = "Kia", Model = "Rio", Year = 2020,
            };
            var job = new JobCard { Vehicle = vehicle, DateIn = Today, Status = JobStatus.Paid };
            for (var i = 1; i <= 40; i++)
            {
                job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = $"Part number {i:00}", Quantity = 1, UnitPrice = 10 });
            }
            db.JobCards.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}"));

        for (var i = 1; i <= 40; i++)
        {
            Assert.Contains($"Part number {i:00}", html);
        }
        Assert.Matches(@"data-invoice-total[^>]*>.*?400\.00", Regex.Replace(html, @"\s+", " "));
    }

    // ---- tenant isolation and access (CLAUDE.md rule 2) ----

    [Fact]
    public async Task Shop_B_can_never_open_shop_As_invoice()
    {
        var (_, shopA, jobA) = await SeedAsync("Shop A", JobStatus.Paid);
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        var clientB = factory.CreateClientFor(userB, shopB);

        var response = await clientB.GetAsync($"/Jobs/Invoice/{jobA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Ahmed Hassan", body);
        Assert.DoesNotContain("Brake pads", body);
        Assert.NotEqual(shopA, shopB);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_open_an_invoice()
    {
        var (_, _, jobId) = await SeedAsync("Anon Garage", JobStatus.Paid);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/Jobs/Invoice/{jobId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_a_shop_is_sent_to_create_one()
    {
        var userId = await TestData.CreateUserWithoutShopAsync(factory);
        var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync("/Jobs/Invoice/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/CreateShop", response.Headers.Location!.ToString());
    }

    // ---- Arabic ----

    [Fact]
    public async Task In_arabic_the_invoice_is_right_to_left_with_translated_labels_and_readable_numbers()
    {
        var (userId, shopId, jobId) = await SeedAsync("Arabic Invoice Garage", JobStatus.Paid);
        var client = factory.CreateClientFor(userId, shopId);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var html = await TestData.BodyAsync(await client.GetAsync($"/Jobs/Invoice/{jobId}"));
        var flat = Regex.Replace(html, @"\s+", " ");

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\"", html);
        foreach (var label in new[] { "فاتورة", "رقم الفاتورة", "التاريخ", "العميل", "الوصف", "الكمية", "الإجمالي", "مدفوع", "شكرًا لتعاملكم معنا." })
        {
            Assert.Contains(label, flat);
        }
        // Numbers, phones and amounts are kept left-to-right so they read correctly inside Arabic text.
        Assert.Contains("<span class=\"ltr\">+201012345678</span>", flat);
        Assert.Contains("ج.م", flat);
        Assert.Matches(@"data-invoice-total[^>]*>.*?600\.00", flat);
    }

    // ---- the print stylesheet (it is plain CSS we control, so we can assert on it) ----

    [Fact]
    public void Printing_hides_the_app_chrome_and_uses_an_a4_page()
    {
        var css = Css();
        var print = PrintBlock(css);

        Assert.Matches(@"@page\s*\{[^}]*size:\s*A4", css);
        Assert.Matches(@"@page\s*\{[^}]*margin:\s*1[0-5]mm", css);

        // Everything that is not the page's own content is hidden on paper.
        var hiddenRule = Regex.Match(print, @"([^{}]*)\{[^{}]*display:\s*none\s*!important", RegexOptions.Singleline).Groups[1].Value;
        foreach (var selector in new[] { ".sidebar", ".topbar", ".tabbar", ".toasts", ".app-footer", ".invoice-toolbar" })
        {
            Assert.Contains(selector, print);
        }
        Assert.True(hiddenRule.Length > 0);
    }

    [Fact]
    public void The_paper_is_always_white_with_dark_text_whatever_the_theme()
    {
        var css = Css();
        var print = PrintBlock(css);
        var invoice = Regex.Match(css, @"\.invoice\s*\{(?<body>[^}]*)\}").Groups["body"].Value;

        // On screen the sheet has its own light palette (it does not read the theme tokens for its colours)...
        Assert.Matches(@"background:\s*#fff", invoice);
        Assert.Matches(@"color:\s*var\(--inv-ink\)", invoice);
        // ...and for print the page itself is forced to white on dark.
        Assert.Matches(@"background:\s*#fff\s*!important", print);
        Assert.Matches(@"color:\s*#000\s*!important", print);
    }

    [Fact]
    public void Colours_survive_printing_and_the_invoice_stays_readable_without_them()
    {
        var css = Css();
        var print = PrintBlock(css);

        Assert.Contains("print-color-adjust: exact", print);

        // Without colour the paid/unpaid chip still has a border and a word, and totals have a rule.
        var chip = Regex.Match(css, @"\.inv-status\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Contains("border:", chip);
        Assert.Contains("border-block-start: 2px solid", css);
    }

    [Fact]
    public void Long_invoices_break_across_pages_cleanly()
    {
        var css = Css();

        // The table header repeats on every page, rows are never split, the totals and the footer stay together.
        Assert.Matches(@"\.inv-table thead\s*\{[^}]*display:\s*table-header-group", css);
        Assert.Matches(@"\.inv-table tr\s*\{[^}]*break-inside:\s*avoid", css);
        Assert.Matches(@"\.inv-totals\s*\{[^}]*break-inside:\s*avoid", css);
        Assert.Matches(@"\.inv-foot\s*\{[^}]*break-inside:\s*avoid", css);
    }

    [Fact]
    public void On_paper_the_spacing_is_tightened_so_a_typical_job_fits_one_page()
    {
        // Measured in a browser with the print rules applied: 15 lines used 897px of the 1033px available.
        // This guards the rules that make that true from being deleted by accident.
        var print = PrintBlock(Css());

        Assert.Matches(@"\.inv-table td\s*\{[^}]*padding-block:\s*0\.28rem", print);
        Assert.Matches(@"\.inv-foot\s*\{[^}]*margin-block-start:\s*6mm", print);
        Assert.Matches(@"\.invoice\s*\{[^}]*font-size:\s*10pt", print);
    }

    // ---- helpers ----

    private static string Css() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "css", "site.css"));

    /// <summary>The text of the @media print { ... } block (balanced braces).</summary>
    private static string PrintBlock(string css)
    {
        var start = css.IndexOf("@media print", StringComparison.Ordinal);
        Assert.True(start >= 0, "No @media print block");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            if (css[i] == '}' && --depth == 0)
            {
                return css[open..(i + 1)];
            }
        }
        throw new InvalidOperationException("Unbalanced @media print block");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GarageDesk.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private async Task<(string UserId, int ShopId, int JobId)> SeedAsync(string shopName, JobStatus status, string? notes = null)
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, shopName);
        using var db = factory.CreateContextFor(shopId);

        // Give the shop real contact details (the shared helper uses placeholders).
        var shop = await db.Shops.SingleAsync();
        shop.Phone = "+201001234567";
        shop.Address = "12 Nile Corniche, Cairo";

        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = "Ahmed Hassan", Phone = "+201012345678" },
            Plate = "ABC 1234", PlateKey = "ABC1234", Make = "Skoda", Model = "Octavia", Year = 2020, Mileage = 120_000,
        };
        var job = new JobCard
        {
            Vehicle = vehicle, DateIn = new DateOnly(2026, 10, 1), MileageAtVisit = 120_350, Status = status, Notes = notes,
        };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Brake pads", Quantity = 2, UnitPrice = 150m });
        job.Lines.Add(new JobLine { Type = JobLineType.Labor, Description = "Fit brake pads", Quantity = 1.5m, UnitPrice = 200m });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();

        return (userId, shopId, job.Id);
    }
}
