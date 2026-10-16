using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GarageDesk.Tests;

/// <summary>
/// brand-logo: how a shop's own logo is shown (the plate in the rail, the phone header, the hero, the invoice and the
/// share page), the logo preview and upload guidance on Shop settings, and the GarageDesk product mark. Presentation
/// only: nothing here changes who may open what (RolesAndPoliciesTests and the tenant tests still own that).
/// </summary>
public partial class BrandLogoTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static readonly string WebRoot = FindWebRoot();

    private static string FindWebRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GarageDesk.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "GarageDesk.Web", "wwwroot");
    }

    private static string Css() => File.ReadAllText(Path.Combine(WebRoot, "css", "site.css"));

    private HttpClient Anonymous() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, string UserId, int ShopId)> OwnerAsync(string shopName, string? logoVersion = null, string? culture = null)
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, shopName);
        var client = factory.CreateClientFor(userId, shopId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, shopName);
        if (logoVersion is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ShopLogoHeader, logoVersion);
        }
        if (culture is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        }
        return (client, userId, shopId);
    }

    private async Task StoreLogoAsync(int shopId, byte[] bytes, string contentType = "image/png")
    {
        using var db = factory.CreateContextFor(shopId);
        db.ShopLogos.Add(new ShopLogo { Bytes = bytes, ContentType = contentType, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task<string> RawAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ======================================================================== the rail header

    [Fact]
    public async Task The_rail_shows_the_logo_on_a_plate_with_the_shop_name_under_it_and_GarageDesk_as_the_small_line()
    {
        var (client, _, _) = await OwnerAsync("Plate Garage", "4242");

        var html = await RawAsync(client, "/");
        var rail = html[html.IndexOf("<aside class=\"sidebar\"", StringComparison.Ordinal)..];
        rail = rail[..rail.IndexOf("</a>", StringComparison.Ordinal)];

        Assert.Contains("class=\"sidebar-brand has-logo\"", rail);
        Assert.Contains("rail-brand-top", rail);
        Assert.Contains("logo-plate logo-plate--rail", rail);
        Assert.Contains("class=\"logo-plate-img\" src=\"/shop/logo?v=4242\" alt=\"Plate Garage\"", rail);
        Assert.Contains("<span class=\"rail-brand-name\"><bdi>Plate Garage</bdi></span>", rail);
        Assert.Contains("<span class=\"rail-brand-sub\">GarageDesk</span>", rail);
        Assert.DoesNotContain("rail-lockup", rail);
    }

    [Fact]
    public async Task Without_a_logo_the_rail_shows_the_GarageDesk_mark_and_wordmark_and_requests_no_image()
    {
        var (client, _, _) = await OwnerAsync("No Logo Garage");

        var html = await RawAsync(client, "/");
        var rail = html[html.IndexOf("<aside class=\"sidebar\"", StringComparison.Ordinal)..];
        rail = rail[..rail.IndexOf("</a>", StringComparison.Ordinal)];

        Assert.Contains("rail-lockup", rail);
        Assert.Contains("class=\"brand-mark\"", rail);
        Assert.Contains("class=\"brand-word\"", rail);
        Assert.Contains("rail-brand-name\"><bdi>No Logo Garage<", rail);
        Assert.DoesNotContain("logo-plate", html);
        Assert.DoesNotContain("has-logo", html);
        Assert.DoesNotContain("/shop/logo", html);
    }

    [Fact]
    public async Task The_shop_name_is_encoded_in_the_alt_text_and_the_logo_url_is_requested_once()
    {
        var (client, _, _) = await OwnerAsync("Tom &amp Jerry <b>", "9");

        var html = await RawAsync(client, "/");

        Assert.DoesNotContain("<b>", html.Replace("<b>Tom", "")); // no raw tag from the name anywhere
        Assert.Contains("alt=\"Tom &amp;amp Jerry &lt;b&gt;\"", html);
        // every place that shows the logo uses the same URL, so the browser fetches it once
        var sources = LogoSource().Matches(html).Select(m => m.Value).Distinct().ToList();
        Assert.Equal(["src=\"/shop/logo?v=9\""], sources);
    }

    [Fact]
    public async Task The_logo_plate_keeps_the_logo_whole_adapts_to_light_and_dark_and_has_a_fixed_rail_height()
    {
        var css = Css();

        // never cropped or squashed
        Assert.Matches(@"\.logo-plate-img\s*\{[^}]*object-fit:\s*contain", css);
        // white boxes of white-background JPEGs melt into the plate
        Assert.Matches(@"\.logo-plate-img\s*\{[^}]*mix-blend-mode:\s*multiply", css);
        // a plate for dark mode too
        Assert.Matches(@"\[data-bs-theme=""dark""\]\s*\.logo-plate\s*\{[^}]*--plate-bg", css);
        // the top of the rail has a fixed height, so the rail never jumps when it opens
        Assert.Matches(@"\.rail-brand-top\s*\{[^}]*height:\s*4\.5rem", css);
        // a visible focus ring on the brand link
        Assert.Contains(".sidebar-brand:focus-visible", css);
        // the old generic wrench tile is gone
        Assert.DoesNotContain(".brand-tile", css);
        // a logo never mirrors in Arabic
        Assert.Matches(@"\.brand-lockup\s*\{[^}]*direction:\s*ltr", css);
        // the invoice logo is shown whole too
        Assert.Matches(@"\.inv-logo\s*\{[^}]*object-fit:\s*contain", css);
    }

    [Fact]
    public async Task In_arabic_the_rail_has_the_logo_plate_and_the_name_follows_its_own_direction()
    {
        var (client, _, _) = await OwnerAsync("ورشة النور", "77", culture: "ar");

        var html = await RawAsync(client, "/");

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("logo-plate logo-plate--rail", html);
        Assert.Contains("rail-brand-name\"><bdi>ورشة النور<", html);
        Assert.Contains("<span class=\"rail-brand-sub\">GarageDesk</span>", html); // the product name stays Latin
    }

    [Fact]
    public async Task The_phone_header_carries_the_shop_logo_or_the_GarageDesk_mark()
    {
        var (withLogo, _, _) = await OwnerAsync("Phone Garage", "5");
        var (without, _, _) = await OwnerAsync("Phone Plain Garage");

        var branded = await RawAsync(withLogo, "/");
        var plain = await RawAsync(without, "/");

        Assert.Matches(@"<a class=""topbar-brand""[^>]*aria-label=""Phone Garage""[^>]*>\s*<span class=""logo-plate logo-plate--sm""", branded);
        Assert.Matches(@"<a class=""topbar-brand""[^>]*>\s*<span class=""topbar-mark"">", plain);
        Assert.DoesNotContain("logo-plate--sm", plain);
        // only shown on phones
        Assert.Matches(@"\.topbar-brand\s*\{[^}]*display:\s*none", Css());
        Assert.Matches(@"@media \(max-width: 767\.98px\)\s*\{\s*\.topbar-brand\s*\{\s*display:\s*inline-flex", Css());
    }

    [Fact]
    public async Task The_dashboard_hero_shows_the_logo_only_when_the_shop_has_one()
    {
        var (withLogo, _, _) = await OwnerAsync("Hero Garage", "6");
        var (without, _, _) = await OwnerAsync("Hero Plain Garage");

        var branded = await RawAsync(withLogo, "/");
        var plain = await RawAsync(without, "/");

        Assert.Matches(@"class=""hero-head"">\s*<span class=""logo-plate logo-plate--hero"">", branded);
        Assert.Contains("hero-head-text", plain);
        Assert.DoesNotContain("logo-plate--hero", plain);
    }

    // ======================================================================== invoice and share page

    [Fact]
    public async Task The_invoice_header_shows_the_logo_whole_and_the_share_page_uses_the_plate()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Docs Garage");
        var jobId = await TestData.SeedJobAsync(factory, shopId, JobStatus.Paid);
        await StoreLogoAsync(shopId, TinyImages.Png(600, 300));
        var token = ShareTokens.Generate();
        var invoiceToken = ShareTokens.Generate();
        using (var db = factory.CreateContextFor(shopId))
        {
            db.ShareLinks.Add(new ShareLink
            {
                Kind = ShareLinkKind.Status, TargetId = jobId, TokenHash = ShareTokens.Hash(token), Language = "en",
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            });
            db.ShareLinks.Add(new ShareLink
            {
                Kind = ShareLinkKind.Invoice, TargetId = jobId, TokenHash = ShareTokens.Hash(invoiceToken), Language = "en",
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        var owner = factory.CreateClientFor(userId, shopId);
        owner.DefaultRequestHeaders.Add(TestAuthHandler.ShopLogoHeader, "31");
        var invoice = await RawAsync(owner, $"/Jobs/Invoice/{jobId}");
        Assert.Contains("class=\"inv-logo\" src=\"/shop/logo?v=31\"", invoice);

        var status = await Anonymous().GetAsync($"/s/{token}");
        var statusHtml = await status.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Matches(@"<span class=""logo-plate logo-plate--card""><img class=""logo-plate-img"" src=""/s/[^""]+\?handler=logo"" alt=""Docs Garage""", statusHtml);

        var shared = await Anonymous().GetAsync($"/s/{invoiceToken}");
        var sharedHtml = await shared.Content.ReadAsStringAsync();
        Assert.Contains("class=\"inv-logo\"", sharedHtml);

        // the customer's page keeps its privacy rules: no manifest, no service worker, no install banner, no cache
        Assert.DoesNotContain("manifest", statusHtml);
        Assert.DoesNotContain("gd-sw", statusHtml);
        Assert.Contains("no-store", status.Headers.CacheControl!.ToString());
        Assert.Contains("noindex", status.Headers.GetValues("X-Robots-Tag").Single());
        // but its tab has the GarageDesk icon
        Assert.Matches(@"href=""/icons/icon[^""]*\.svg""", statusHtml);
        Assert.Matches(@"href=""/icons/favicon-32[^""]*\.png""", statusHtml);
    }

    // ======================================================================== Shop settings: guidance, size hint, preview

    [Fact]
    public async Task Shop_settings_without_a_logo_shows_the_upload_tips_and_no_preview()
    {
        var (client, _, _) = await OwnerAsync("Tips Garage");

        var html = await RawAsync(client, "/ShopSettings");

        Assert.Contains("data-logo-tips", html);
        Assert.Contains("transparent PNG", html);
        Assert.Contains("512 × 512", html);
        Assert.Contains("1024 × 256", html);
        Assert.Contains("under 512 KB", html);
        Assert.DoesNotContain("data-logo-preview", html);
        Assert.DoesNotContain("data-logo-small", html);
        Assert.Contains("class=\"brand-mark\"", html); // the GarageDesk mark stands in for "no logo yet"
    }

    [Fact]
    public async Task Shop_settings_with_a_logo_previews_it_in_the_menu_on_the_plate_on_an_invoice_and_on_a_phone()
    {
        var (client, _, shopId) = await OwnerAsync("Preview Garage", "88");
        await StoreLogoAsync(shopId, TinyImages.Png(640, 480));

        var html = await RawAsync(client, "/ShopSettings");

        Assert.Contains("data-logo-preview", html);
        Assert.Equal(4, Regex.Matches(html, "<figure class=\"logo-frame\">").Count);
        foreach (var caption in new[] { "In the dark menu", "On the light plate", "On an invoice", "On a phone" })
        {
            Assert.Contains($"<figcaption>{caption}</figcaption>", html);
        }

        Assert.Contains("lf-dark", html);
        Assert.Contains("lf-paper", html);
        Assert.Contains("lf-phone", html);
        Assert.Contains("data-logo-size>Your logo is 640 × 480 px.", html);
        Assert.DoesNotContain("data-logo-small", html);
        // the preview reuses the shop's own logo URL: no extra request
        Assert.Equal(["src=\"/shop/logo?v=88\""], LogoSource().Matches(html).Select(m => m.Value).Distinct().ToList());
    }

    [Theory]
    [InlineData(255, 600, true)]
    [InlineData(600, 128, true)]
    [InlineData(96, 96, true)]
    [InlineData(256, 256, false)]
    [InlineData(1024, 256, false)]
    public async Task A_logo_under_256_pixels_on_its_short_side_gets_a_gentle_hint(int width, int height, bool hint)
    {
        var (client, _, shopId) = await OwnerAsync($"Size {width}x{height} Garage", "12");
        await StoreLogoAsync(shopId, TinyImages.PngHeaderOnly(width, height));

        var html = await RawAsync(client, "/ShopSettings");

        Assert.Equal(hint, html.Contains("data-logo-small"));
        if (hint)
        {
            Assert.Contains("This logo is small and may look blurry.", html);
            Assert.Contains("role=\"note\"", html);
        }
        Assert.Contains($"Your logo is {width} × {height} px.", html);
    }

    [Fact]
    public async Task A_small_logo_is_still_accepted_the_hint_never_blocks_an_upload()
    {
        var (client, _, shopId) = await OwnerAsync("Small Upload Garage", "13");
        var token = await TokenAsync(client);
        var file = new ByteArrayContent(TinyImages.Png(64, 64));
        file.Headers.ContentType = new("image/png");
        var form = new MultipartFormDataContent { { file, "upload", "tiny.png" }, { new StringContent(token), "__RequestVerificationToken" } };

        var response = await client.PostAsync("/ShopSettings?handler=Upload", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = factory.CreateContextFor(shopId);
        Assert.Single(db.ShopLogos.ToList());
        var html = await RawAsync(client, "/ShopSettings");
        Assert.Contains("data-logo-small", html);
    }

    [Fact]
    public async Task A_logo_whose_header_cannot_be_read_shows_no_size_and_no_hint_but_still_previews()
    {
        var (client, _, shopId) = await OwnerAsync("Odd Header Garage", "14");
        // a valid-looking PNG start with a truncated header: it was accepted at upload, but its size cannot be read
        await StoreLogoAsync(shopId, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52]);

        var html = await RawAsync(client, "/ShopSettings");

        Assert.DoesNotContain("data-logo-size", html);
        Assert.DoesNotContain("data-logo-small", html);
        Assert.Contains("data-logo-preview", html);
    }

    // ======================================================================== the GarageDesk product mark

    [Fact]
    public async Task The_product_mark_is_on_the_login_register_landing_join_and_offline_pages()
    {
        var anonymous = Anonymous();

        foreach (var path in new[] { "/", "/Identity/Account/Login", "/Identity/Account/Register" })
        {
            var html = await RawAsync(anonymous, path);
            Assert.Contains("class=\"brand-lockup\"", html);
            Assert.Contains("class=\"brand-mark\"", html);
            Assert.Contains("class=\"brand-word\"", html);
            Assert.Contains("class=\"gd-needle\"", html);
            Assert.DoesNotContain("brand-tile", html);
        }

        // the split-screen brand panel has the larger lockup
        Assert.Contains("public-brand public-brand--lg", await RawAsync(anonymous, "/Identity/Account/Login"));

        // the landing page also marks its hero and its footer
        var landing = await RawAsync(anonymous, "/");
        Assert.Contains("landing-mark", landing);
        Assert.Contains("footer-brand", landing);

        // the join page (public header)
        var join = await anonymous.GetAsync("/Join/not-a-real-token");
        Assert.Contains("class=\"brand-lockup\"", await join.Content.ReadAsStringAsync());

        // the offline page is static HTML with the mark inlined (it must work with nothing else cached)
        var offline = await RawAsync(anonymous, "/offline.html");
        Assert.Contains("aria-label=\"GarageDesk\"", offline);
        Assert.Contains("stroke=\"#f97316\"", offline);
        Assert.Contains("class=\"word\"", offline);
        Assert.DoesNotContain("<script", offline);
        Assert.DoesNotContain("M14.7 6.3", offline); // the old wrench glyph
    }

    [Fact]
    public async Task No_view_still_draws_the_old_wrench_tile()
    {
        var views = Directory.EnumerateFiles(Path.Combine(WebRoot, "..", "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(WebRoot, "..", "Areas"), "*.cshtml", SearchOption.AllDirectories));

        foreach (var view in views)
        {
            Assert.DoesNotContain("brand-tile", await File.ReadAllTextAsync(view));
        }
    }

    [Fact]
    public async Task The_mark_takes_the_shop_accent_only_on_its_needle()
    {
        var css = Css();

        // the G, the tile and the wordmark never change: only the needle variable is re-pointed to the accent
        Assert.Matches(@"\.brand-mark--tint,\s*\.tint-accent \.brand-mark\s*\{\s*--gd-needle:\s*var\(--accent\)", css);
        Assert.Matches(@"\.rail-lockup-mark \.brand-mark\s*\{[^}]*--gd-needle:\s*var\(--accent\)", css);
        Assert.DoesNotMatch(@"--gd-arc:\s*var\(--accent\)", css);
        Assert.DoesNotMatch(@"--gd-tile:\s*var\(--accent\)", css);

        // and the signed-in rail uses it, with the accent coming from the login as before
        var (client, _, _) = await OwnerAsync("Teal Garage");
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopAccentHeader, "teal");
        var html = await RawAsync(client, "/");
        Assert.Contains("data-accent=\"teal\"", html);
        Assert.Contains("rail-lockup-mark", html);
    }

    // ======================================================================== favicon, icons, manifest, service worker

    [Fact]
    public async Task Every_page_family_links_the_svg_favicon_the_png_fallbacks_and_the_files_exist()
    {
        var anonymous = Anonymous();
        var (signedIn, _, _) = await OwnerAsync("Icon Garage");

        var landing = await RawAsync(anonymous, "/");
        var dashboard = await RawAsync(signedIn, "/");
        var login = await RawAsync(anonymous, "/Identity/Account/Login");

        foreach (var html in new[] { landing, dashboard, login })
        {
            Assert.Matches(@"rel=""icon"" href=""/favicon[^""]*\.ico""", html);
            Assert.Matches(@"rel=""icon"" type=""image/svg\+xml"" href=""/icons/icon[^""]*\.svg""", html);
            Assert.Matches(@"rel=""icon"" type=""image/png"" sizes=""32x32"" href=""/icons/favicon-32[^""]*\.png""", html);
            Assert.Matches(@"rel=""apple-touch-icon"" href=""/icons/apple-touch-icon[^""]*png""", html);
            Assert.Contains("name=\"theme-color\" content=\"#0b1324\"", html);
        }

        foreach (var (path, type) in new[]
        {
            ("/favicon.ico", "image/x-icon"), ("/icons/icon.svg", "image/svg+xml"), ("/icons/icon-maskable.svg", "image/svg+xml"),
            ("/icons/favicon-32.png", "image/png"), ("/icons/apple-touch-icon.png", "image/png"),
            ("/icons/icon-192.png", "image/png"), ("/icons/icon-512.png", "image/png"), ("/icons/icon-maskable-512.png", "image/png"),
        })
        {
            var response = await anonymous.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} -> {response.StatusCode}");
            Assert.Contains(type.Split('/')[1].Split('-')[^1], response.Content.Headers.ContentType!.MediaType!); // png / svg+xml / icon
        }
    }

    [Theory]
    [InlineData("/icons/favicon-32.png", 32)]
    [InlineData("/icons/apple-touch-icon.png", 180)]
    [InlineData("/icons/icon-192.png", 192)]
    [InlineData("/icons/icon-512.png", 512)]
    [InlineData("/icons/icon-maskable-512.png", 512)]
    public async Task The_png_icons_are_square_and_the_size_their_name_says(string path, int size)
    {
        var bytes = await Anonymous().GetByteArrayAsync(path);

        Assert.Equal<(int, int)?>((size, size), ImageSize.TryRead(bytes));
    }

    [Fact]
    public async Task The_favicon_ico_packs_16_32_and_48_pixel_png_images()
    {
        var bytes = await Anonymous().GetByteArrayAsync("/favicon.ico");

        Assert.Equal([0, 0, 1, 0], bytes[..4]);           // reserved, type 1 = icon
        var count = bytes[4] | (bytes[5] << 8);
        Assert.Equal(3, count);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            sizes.Add(bytes[entry]);
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[offset..(offset + 4)]); // each image is a PNG
            Assert.True(offset + length <= bytes.Length);
        }
        Assert.Equal([16, 32, 48], sizes);
    }

    [Fact]
    public async Task The_svg_icons_are_plain_vector_files_with_the_new_mark_and_the_maskable_one_respects_the_safe_zone()
    {
        var client = Anonymous();
        var icon = await client.GetStringAsync("/icons/icon.svg");
        var maskable = await client.GetStringAsync("/icons/icon-maskable.svg");

        foreach (var svg in new[] { icon, maskable })
        {
            Assert.StartsWith("<svg", svg);
            Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onload", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("M14.7 6.3", svg); // not the old wrench
            Assert.Contains("#f97316", svg);          // the needle
            Assert.Contains("A20 20 0 1 0", svg);     // the gauge arc
        }

        // The icon is a rounded tile; the maskable one is full bleed (the OS applies its own shape)...
        Assert.Contains("rx=\"17\"", icon);
        Assert.DoesNotContain("rx=\"17\"", maskable);
        // ...and the mark's outer radius (arc radius 20 + half the stroke 4 = 24 units of 64) must stay within the
        // maskable safe zone, a circle of 40% of the icon size (25.6 units).
        var scale = double.Parse(Regex.Match(maskable, @"scale\(([0-9.]+)\)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(24 * scale <= 25.6, $"The mark ({24 * scale} units) pokes out of the maskable safe zone (25.6).");
    }

    [Fact]
    public async Task The_manifest_colours_match_the_icon_and_the_icons_it_lists_exist()
    {
        var client = Anonymous();
        using var manifest = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/manifest.webmanifest"));
        var icon = await client.GetStringAsync("/icons/icon.svg");
        var tile = Regex.Match(icon, @"<rect[^>]*fill=""(#[0-9a-fA-F]{6})""").Groups[1].Value;

        // the splash background is the icon's own tile colour, so launching the app shows no seam
        Assert.Equal(tile, manifest.RootElement.GetProperty("background_color").GetString(), ignoreCase: true);
        Assert.Equal("#0b1324", manifest.RootElement.GetProperty("theme_color").GetString());

        foreach (var entry in manifest.RootElement.GetProperty("icons").EnumerateArray())
        {
            var response = await client.GetAsync(entry.GetProperty("src").GetString());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_service_worker_precache_list_only_names_files_that_exist_and_are_cacheable()
    {
        var client = Anonymous();
        var sw = await client.GetStringAsync("/sw.js");

        var offlineUrl = Regex.Match(sw, @"const OFFLINE_URL = ""([^""]+)""").Groups[1].Value;
        var list = Regex.Match(sw, @"cache\.addAll\(\[([^\]]+)\]\)").Groups[1].Value;
        var entries = list.Split(',').Select(e => e.Trim()).Select(e => e == "OFFLINE_URL" ? offlineUrl : e.Trim('"')).ToList();

        Assert.Contains(offlineUrl, entries);
        Assert.Contains(entries, e => e.StartsWith("/icons/"));
        var staticPrefixes = Regex.Match(sw, @"STATIC_PREFIXES = \[([^\]]+)\]").Groups[1].Value.Split(',').Select(p => p.Trim().Trim('"')).ToList();
        foreach (var entry in entries)
        {
            var response = await client.GetAsync(entry);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"The service worker precaches {entry} but it is {response.StatusCode}.");
            Assert.True(entry == offlineUrl || staticPrefixes.Any(entry.StartsWith), $"{entry} is precached but is not in a cacheable folder.");
        }

        // a new cache name drops the copies of the old icons on devices that installed the app before
        Assert.DoesNotContain("\"gd-static-v1\"", sw);
    }

    [Fact]
    public async Task The_brand_files_exist_and_are_plain_vector_images()
    {
        var client = Anonymous();
        var inventory = new[]
        {
            "garagedesk-mark.svg", "garagedesk-mark-bare.svg", "garagedesk-mark-bare-light.svg", "garagedesk-mark-mono.svg",
            "garagedesk-lockup.svg", "garagedesk-lockup-light.svg", "garagedesk-lockup-stacked.svg",
            "garagedesk-lockup-stacked-light.svg", "garagedesk-lockup-mono.svg",
        };

        foreach (var name in inventory)
        {
            var response = await client.GetAsync($"/brand/{name}");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"/brand/{name} -> {response.StatusCode}");
            var svg = await response.Content.ReadAsStringAsync();
            Assert.StartsWith("<svg", svg);
            Assert.Contains("aria-label=\"GarageDesk\"", svg);
            Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<image", svg, StringComparison.OrdinalIgnoreCase);        // vector only, nothing embedded
            Assert.DoesNotContain("<text", svg, StringComparison.OrdinalIgnoreCase);         // the wordmark is outlined, no font needed
        }

        // the lockups carry the wordmark; the dark-page versions turn it white
        Assert.Contains("--gd-word: #0b1324", await client.GetStringAsync("/brand/garagedesk-lockup.svg"));
        Assert.Contains("--gd-word: #ffffff", await client.GetStringAsync("/brand/garagedesk-lockup-light.svg"));
    }

    [GeneratedRegex(@"src=""/shop/logo\?v=[0-9]+""")]
    private static partial Regex LogoSource();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/ShopSettings");
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, "No anti-forgery token on /ShopSettings");
        return match.Groups[1].Value;
    }
}
