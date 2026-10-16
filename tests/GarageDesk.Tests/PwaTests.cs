using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GarageDesk.Tests;

/// <summary>Growth: the installable app. The service worker's rules are pinned here so a later edit cannot quietly start caching pages.</summary>
public partial class PwaTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private HttpClient Anonymous() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task The_manifest_is_served_with_the_fields_a_browser_needs_to_install_the_app()
    {
        var response = await Anonymous().GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("GarageDesk", root.GetProperty("name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.StartsWith("#", root.GetProperty("theme_color").GetString());
        Assert.StartsWith("#", root.GetProperty("background_color").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.GetProperty("purpose").GetString() == "any");
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
    }

    [Fact]
    public async Task Every_icon_in_the_manifest_exists_and_the_png_sizes_are_what_they_claim()
    {
        var client = Anonymous();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/manifest.webmanifest"));

        foreach (var icon in json.RootElement.GetProperty("icons").EnumerateArray())
        {
            var src = icon.GetProperty("src").GetString()!;
            var response = await client.GetAsync(src);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{src} -> {response.StatusCode}");
            Assert.Equal(icon.GetProperty("type").GetString(), response.Content.Headers.ContentType!.MediaType);

            if (icon.GetProperty("type").GetString() == "image/png")
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);
                var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                Assert.Equal(icon.GetProperty("sizes").GetString(), $"{width}x{height}");
            }
        }

        var apple = await client.GetAsync("/icons/apple-touch-icon.png");
        Assert.Equal(HttpStatusCode.OK, apple.StatusCode);
    }

    [Fact]
    public async Task The_service_worker_is_served_from_the_root_as_javascript()
    {
        var response = await Anonymous().GetAsync("/sw.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType!.MediaType);
        // The worker script itself must never be cached for a long time, or a fix could not reach phones.
        Assert.DoesNotContain("immutable", response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task The_service_worker_never_touches_share_invite_login_or_logo_requests()
    {
        var sw = await Anonymous().GetStringAsync("/sw.js");

        // The "leave it to the browser" list.
        foreach (var path in new[] { "\"/s/\"", "\"/join/\"", "\"/identity/\"", "\"/shop/\"", "\"/join\"", "\"/identity\"" })
        {
            Assert.Contains(path, sw);
        }

        // It is checked before anything else can respond.
        var neverCheck = sw.IndexOf("if (isNever(url.pathname)) return;", StringComparison.Ordinal);
        var navigate = sw.IndexOf("request.mode === \"navigate\"", StringComparison.Ordinal);
        var staticCheck = sw.IndexOf("isStatic(url.pathname)", StringComparison.Ordinal);
        Assert.True(neverCheck > 0 && neverCheck < navigate && neverCheck < staticCheck, "The never-list must be checked first.");

        // Only plain same-site GETs are handled at all.
        Assert.Contains("request.method !== \"GET\"", sw);
        Assert.Contains("url.origin !== self.location.origin", sw);
    }

    [Fact]
    public async Task The_service_worker_stores_static_files_only_and_never_a_page()
    {
        var sw = await Anonymous().GetStringAsync("/sw.js");

        // One single place writes to the cache (plus the install-time precache of two fixed files)...
        Assert.Single(PutCall().Matches(sw));
        Assert.Single(AddAllCall().Matches(sw));

        // ...it is guarded: html, no-store, private and cookies are refused...
        Assert.Contains("text/html", sw);
        Assert.Contains("no-store", sw);
        Assert.Contains("private", sw);
        Assert.Contains("Set-Cookie", sw);

        // ...only these folders are "static", and the page navigation branch has no cache write in it.
        Assert.Contains("[\"/css/\", \"/js/\", \"/lib/\", \"/icons/\", \"/fonts/\"]", sw);
        var navigateBranch = sw[sw.IndexOf("request.mode === \"navigate\"", StringComparison.Ordinal)..];
        navigateBranch = navigateBranch[..navigateBranch.IndexOf("return;", StringComparison.Ordinal)];
        Assert.DoesNotContain("put(", navigateBranch);
        Assert.Contains("fetch(request).catch(() => caches.match(OFFLINE_URL))", navigateBranch);

        // The only things installed ahead of time are the offline page and one icon.
        Assert.Contains("[OFFLINE_URL, \"/icons/icon-192.png\"]", sw);
    }

    [Fact]
    public async Task The_offline_page_is_static_and_bilingual_and_holds_no_shop_data()
    {
        var response = await Anonymous().GetAsync("/offline.html");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You are offline", html);
        Assert.Contains("أنت غير متصل بالإنترنت", html);
        Assert.Contains("noindex", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Pages_link_the_manifest_and_icons_and_the_registration_script_ships()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Pwa Garage");

        var landing = await TestData.BodyAsync(await Anonymous().GetAsync("/"));
        var dashboard = await TestData.BodyAsync(await factory.CreateClientFor(userId, shop).GetAsync("/"));

        foreach (var html in new[] { landing, dashboard })
        {
            Assert.Matches("rel=\"manifest\" href=\"/manifest[^\"]*webmanifest\"", html);
            Assert.Matches("rel=\"apple-touch-icon\" href=\"/icons/apple-touch-icon[^\"]*png\"", html);
            Assert.Contains("name=\"theme-color\"", html);
            Assert.Contains("name=\"gd-sw\" content=\"/sw.js\"", html);
        }

        var js = await Anonymous().GetStringAsync("/js/site.js");
        Assert.Contains("serviceWorker", js);
        Assert.Contains("beforeinstallprompt", js);
    }

    [Fact]
    public async Task The_customers_share_page_does_not_register_a_worker_or_offer_installing()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Share Pwa Garage");
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done);
        var token = ShareTokens.Generate();
        using (var db = factory.CreateContextFor(shop))
        {
            db.ShareLinks.Add(new ShareLink
            {
                Kind = ShareLinkKind.Status, TargetId = job, TokenHash = ShareTokens.Hash(token), Language = "en",
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        var response = await Anonymous().GetAsync($"/s/{token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("manifest", html);
        Assert.DoesNotContain("gd-sw", html);
        // and its protective headers are untouched
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [GeneratedRegex(@"\.put\(")]
    private static partial Regex PutCall();

    [GeneratedRegex(@"\.addAll\(")]
    private static partial Regex AddAllCall();
}
