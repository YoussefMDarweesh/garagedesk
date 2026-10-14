using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GarageDesk.Web;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Tests;

public partial class ShopLogoTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // A real 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // A different valid-looking PNG (extra bytes after the header) so tests can tell two logos apart.
    private static readonly byte[] OtherPng = [.. Png, .. "other-shop"u8.ToArray()];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8.ToArray(), 0x00, 0x01, 0xFF, 0xD9];

    private static readonly byte[] WebP = [.. "RIFF"u8.ToArray(), 0x1A, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8.ToArray(), 0x0E, 0x00, 0x00, 0x00];

    // ---- accepting uploads ----

    [Fact]
    public async Task A_valid_png_is_stored_and_served_back_to_its_shop()
    {
        var (userId, shopId) = await CreateOwnerAsync("Png Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await UploadAsync(client, Png);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = await StoredLogoAsync(shopId);
        Assert.NotNull(stored);
        Assert.Equal("image/png", stored!.ContentType);
        Assert.Equal(Png, stored.Bytes);

        var served = await client.GetAsync("/shop/logo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("private", served.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public async Task Jpeg_and_webp_are_accepted(string kind, string expectedType)
    {
        var (userId, shopId) = await CreateOwnerAsync($"{kind} Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await UploadAsync(client, kind == "jpeg" ? Jpeg : WebP, $"logo.{kind}", expectedType);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedType, (await StoredLogoAsync(shopId))!.ContentType);
    }

    [Fact]
    public async Task The_type_comes_from_the_bytes_not_from_what_the_browser_claims()
    {
        var (userId, shopId) = await CreateOwnerAsync("Honest Garage");
        var client = factory.CreateClientFor(userId, shopId);

        // A real PNG that the browser labelled as a JPEG.
        await UploadAsync(client, Png, "logo.jpg", "image/jpeg");

        Assert.Equal("image/png", (await StoredLogoAsync(shopId))!.ContentType);
    }

    [Fact]
    public async Task Uploading_again_replaces_the_logo()
    {
        var (userId, shopId) = await CreateOwnerAsync("Replace Garage");
        var client = factory.CreateClientFor(userId, shopId);

        await UploadAsync(client, Png);
        await UploadAsync(client, Jpeg, "logo.jpg", "image/jpeg");

        var stored = await StoredLogoAsync(shopId);
        Assert.Equal("image/jpeg", stored!.ContentType);
        Assert.Equal(Jpeg, stored.Bytes);
    }

    // ---- refusing uploads ----

    [Fact]
    public async Task An_oversized_image_is_refused_with_a_clear_message()
    {
        var (userId, shopId) = await CreateOwnerAsync("Big Garage");
        var client = factory.CreateClientFor(userId, shopId);
        var tooBig = new byte[ShopLogoImage.MaxBytes + 1];
        Png.CopyTo(tooBig, 0); // starts like a real PNG, so only the size is wrong

        var response = await UploadAsync(client, tooBig);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The image is too large. The maximum size is 512 KB.", await BodyAsync(response));
        Assert.Null(await StoredLogoAsync(shopId));
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        var (userId, shopId) = await CreateOwnerAsync("Text Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await UploadAsync(client, "just some notes"u8.ToArray(), "notes.txt", "text/plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Only PNG, JPEG or WebP images are allowed.", await BodyAsync(response));
        Assert.Null(await StoredLogoAsync(shopId));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "logo.png", "image/png")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "logo.svg", "image/svg+xml")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "logo.png", "image/png")]
    public async Task A_spoofed_file_is_refused_even_if_it_is_named_and_labelled_as_an_image(
        string content, string fileName, string claimedType)
    {
        var (userId, shopId) = await CreateOwnerAsync("Spoof Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(content), fileName, claimedType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Only PNG, JPEG or WebP images are allowed.", await BodyAsync(response));
        Assert.Null(await StoredLogoAsync(shopId));
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var (userId, shopId) = await CreateOwnerAsync("Empty Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await UploadAsync(client, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose an image to upload.", await BodyAsync(response));
    }

    // ---- removing ----

    [Fact]
    public async Task The_logo_can_be_removed()
    {
        var (userId, shopId) = await CreateOwnerAsync("Remove Garage");
        var client = factory.CreateClientFor(userId, shopId);
        await UploadAsync(client, Png);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/shop/logo")).StatusCode);

        var token = await TokenAsync(client, "/ShopSettings");
        var response = await client.PostAsync("/ShopSettings?handler=Remove", new FormUrlEncodedContent(
            [new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredLogoAsync(shopId));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/shop/logo")).StatusCode);
    }

    // ---- tenant isolation (CLAUDE.md rule 2) ----

    [Fact]
    public async Task Shop_B_can_never_fetch_shop_As_logo()
    {
        var (userA, shopA) = await CreateOwnerAsync("Shop A");
        var (userB, shopB) = await CreateOwnerAsync("Shop B");
        var clientA = factory.CreateClientFor(userA, shopA);
        var clientB = factory.CreateClientFor(userB, shopB);
        await UploadAsync(clientA, Png);

        // B has no logo of its own, so there is nothing for B to see: A's logo does not leak.
        var asB = await clientB.GetAsync("/shop/logo");
        Assert.Equal(HttpStatusCode.NotFound, asB.StatusCode);

        // Once B uploads its own, B gets B's bytes and A still gets A's.
        await UploadAsync(clientB, OtherPng);
        Assert.Equal(OtherPng, await clientB.GetByteArrayAsync("/shop/logo"));
        Assert.Equal(Png, await clientA.GetByteArrayAsync("/shop/logo"));
    }

    [Fact]
    public async Task There_is_no_way_to_ask_for_another_shops_logo_by_id()
    {
        var (userA, shopA) = await CreateOwnerAsync("Shop A2");
        var (userB, shopB) = await CreateOwnerAsync("Shop B2");
        await UploadAsync(factory.CreateClientFor(userA, shopA), Png);
        var clientB = factory.CreateClientFor(userB, shopB);

        // The endpoint ignores any id-like input; it only ever uses the signed-in user's shop.
        foreach (var url in new[] { $"/shop/logo?shopId={shopA}", $"/shop/logo?id={shopA}", $"/shop/{shopA}/logo" })
        {
            var response = await clientB.GetAsync(url);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_gets_no_logo()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/shop/logo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_a_shop_has_no_logo_and_no_settings_page()
    {
        var userId = await CreateUserWithoutShopAsync();
        var client = factory.CreateClientFor(userId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/shop/logo")).StatusCode);

        var settings = await client.GetAsync("/ShopSettings");
        Assert.Equal(HttpStatusCode.Redirect, settings.StatusCode);
        Assert.StartsWith("/CreateShop", settings.Headers.Location!.ToString());
    }

    // ---- display ----

    [Fact]
    public async Task The_navbar_shows_the_logo_only_when_the_shop_has_one()
    {
        var (userId, shopId) = await CreateOwnerAsync("Logo Garage");
        var withLogo = factory.CreateClientFor(userId, shopId);
        withLogo.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, "Logo Garage");
        withLogo.DefaultRequestHeaders.Add(TestAuthHandler.ShopLogoHeader, "12345");
        var without = factory.CreateClientFor(userId, shopId);

        var htmlWith = await (await withLogo.GetAsync("/")).Content.ReadAsStringAsync();
        var htmlWithout = await (await without.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("src=\"/shop/logo?v=12345\"", htmlWith);
        Assert.Contains("alt=\"Logo Garage\"", htmlWith);
        Assert.Contains("brand-tile has-logo", htmlWith);
        Assert.DoesNotContain("/shop/logo", htmlWithout);
        Assert.DoesNotContain("has-logo", htmlWithout);
    }

    [Fact]
    public async Task The_login_cookie_carries_the_logo_version_after_an_upload()
    {
        var (userId, shopId) = await CreateOwnerAsync("Cookie Garage");
        await UploadAsync(factory.CreateClientFor(userId, shopId), Png);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        var principal = await claimsFactory.CreateAsync((await users.FindByIdAsync(userId))!);

        var version = principal.FindFirst(CurrentShop.LogoClaimType)?.Value;
        Assert.True(long.TryParse(version, out var ticks) && ticks > 0);
    }

    // ---- the image sniffer on its own ----

    [Fact]
    public void The_sniffer_recognises_the_three_formats_and_nothing_else()
    {
        Assert.Equal("image/png", ShopLogoImage.Detect(Png));
        Assert.Equal("image/jpeg", ShopLogoImage.Detect(Jpeg));
        Assert.Equal("image/webp", ShopLogoImage.Detect(WebP));

        Assert.Null(ShopLogoImage.Detect([]));
        Assert.Null(ShopLogoImage.Detect([0x89, 0x50]));                       // truncated PNG signature
        Assert.Null(ShopLogoImage.Detect("GIF89a-not-allowed"u8));
        Assert.Null(ShopLogoImage.Detect("<svg xmlns='http://www.w3.org/2000/svg'/>"u8));
        Assert.Null(ShopLogoImage.Detect("RIFF....WAVEfmt "u8));               // RIFF, but audio not WebP
    }

    // ---- Arabic ----

    [Fact]
    public void The_new_strings_have_arabic_translations()
    {
        var keys = new[]
        {
            "Shop settings", "Shop logo", "PNG, JPEG or WebP, up to 512 KB. It appears in the navigation bar.",
            "No logo yet.", "Logo image", "Upload", "Remove logo", "Choose an image to upload.",
            "The image is too large. The maximum size is 512 KB.", "Only PNG, JPEG or WebP images are allowed.",
            "Logo updated.", "Logo removed.",
        };
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar");
            using var scope = factory.Services.CreateScope();
            var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();

            foreach (var key in keys)
            {
                Assert.False(localizer[key].ResourceNotFound, $"Missing Arabic translation: {key}");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, byte[] bytes, string fileName = "logo.png", string contentType = "image/png")
    {
        var token = await TokenAsync(client, "/ShopSettings");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(contentType);
        var form = new MultipartFormDataContent
        {
            { file, "upload", fileName },
            { new StringContent(token), "__RequestVerificationToken" },
        };
        return await client.PostAsync("/ShopSettings?handler=Upload", form);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {path}");
        return match.Groups[1].Value;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private async Task<ShopLogo?> StoredLogoAsync(int shopId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // This scope has no signed-in user, so look past the tenant filter and ask for the shop explicitly.
        return await db.ShopLogos.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(l => l.ShopId == shopId);
    }

    private async Task<(string UserId, int ShopId)> CreateOwnerAsync(string shopName)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            Shop = new Shop { Name = shopName, Phone = "01000000000", Address = "Cairo" },
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        return (user.Id, user.ShopId!.Value);
    }

    private async Task<string> CreateUserWithoutShopAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AppUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        return user.Id;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
