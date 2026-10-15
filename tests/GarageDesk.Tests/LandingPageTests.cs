using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GarageDesk.Tests;

/// <summary>Growth: the public landing page at "/" for logged-out visitors.</summary>
public partial class LandingPageTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static WebApplicationFactoryClientOptions NoRedirect => new() { AllowAutoRedirect = false };

    private static async Task<string> GetAsync(HttpClient client, string path, string? culture = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (culture is not null)
        {
            request.Headers.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TestData.BodyAsync(response);
    }

    private HttpClient WithWhatsApp(string? number) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (number is not null)
            {
                builder.UseSetting(LandingContact.ConfigKey, number);
            }
        }).CreateClient(NoRedirect);

    // ---- content ----

    [Fact]
    public async Task Visitors_see_the_hero_features_screenshots_and_both_ways_in()
    {
        var html = await GetAsync(factory.CreateClient(NoRedirect), "/");

        Assert.Contains("landing-hero", html);
        Assert.Contains("One place for every job, car and customer.", html);
        Assert.Contains("What GarageDesk does", html);
        foreach (var feature in new[] { "Job cards", "Invoices & estimates", "WhatsApp links", "Payments & balances", "Your team", "Made for your phone" })
        {
            Assert.Contains(feature, html);
        }
        Assert.Contains("See it at work", html);
        Assert.Contains("/Identity/Account/Register", html);
        Assert.Contains("/Identity/Account/Login", html);
        Assert.DoesNotContain("Coming soon", html);
    }

    [Fact]
    public async Task Every_screenshot_on_the_page_exists_and_has_alt_text()
    {
        var client = factory.CreateClient(NoRedirect);
        var html = await GetAsync(client, "/", culture: "ar");

        var images = LandingImage().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.True(images.Count >= 4, "The Arabic page shows the phone screenshot and three desktop ones.");
        foreach (var src in images)
        {
            var response = await client.GetAsync(src);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{src} -> {response.StatusCode}");
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        }

        Assert.DoesNotContain("alt=\"\"", html.Split("<figure")[1..].Aggregate("", (a, b) => a + b));
    }

    [Fact]
    public async Task The_page_is_available_in_arabic_right_to_left()
    {
        var html = await GetAsync(factory.CreateClient(NoRedirect), "/", culture: "ar");

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
        Assert.Contains("مكان واحد لكل أمر شغل وسيارة وعميل.", html);
        Assert.Contains("روابط واتساب", html);
        Assert.Contains("شاهده أثناء العمل", html);
        Assert.Contains("dashboard-ar", html);
    }

    [Fact]
    public async Task The_landing_page_has_its_own_title_and_description_for_search_engines()
    {
        var html = await GetAsync(factory.CreateClient(NoRedirect), "/");

        Assert.Contains("<title>Workshop manager for small car service shops · GarageDesk</title>", html);
        Assert.Contains("name=\"description\" content=\"Job cards, invoices, reminders and WhatsApp links", html);
    }

    // ---- the WhatsApp contact button ----

    [Fact]
    public async Task Without_a_number_configured_there_is_no_whatsapp_button()
    {
        var html = await GetAsync(WithWhatsApp(null), "/");

        Assert.DoesNotContain("data-landing-whatsapp", html);
        Assert.DoesNotContain("wa.me", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    public async Task An_empty_or_unusable_number_hides_the_button(string number)
    {
        var html = await GetAsync(WithWhatsApp(number), "/");

        Assert.DoesNotContain("wa.me", html);
    }

    [Fact]
    public async Task A_configured_number_shows_a_whatsapp_button_with_a_prefilled_message()
    {
        var html = await GetAsync(WithWhatsApp("0101 234 5678"), "/");

        var link = WhatsAppLinkOnPage().Match(html);
        Assert.True(link.Success, "No wa.me link on the page");
        Assert.StartsWith("https://wa.me/201012345678?text=", link.Groups[1].Value);
        Assert.Contains("Talk to us on WhatsApp", html);
        // opens in a new tab without handing the page to the new one
        Assert.Matches(@"data-landing-whatsapp", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public async Task The_whatsapp_message_follows_the_page_language()
    {
        var html = await GetAsync(WithWhatsApp("+201012345678"), "/", culture: "ar");

        var link = WhatsAppLinkOnPage().Match(html).Groups[1].Value;
        Assert.Contains(Uri.EscapeDataString("مرحبًا، أود معرفة المزيد عن GarageDesk."), link);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0101 234 5678", "https://wa.me/201012345678?text=Hi%20there")]
    [InlineData("+20 101 234 5678", "https://wa.me/201012345678?text=Hi%20there")]
    [InlineData("+44 20 7946 0958", "https://wa.me/442079460958?text=Hi%20there")]
    [InlineData("hello", null)]
    public void Contact_links_are_built_from_the_setting(string? configured, string? expected)
    {
        Assert.Equal(expected, LandingContact.WhatsAppUrl(configured, "Hi there"));
    }

    // ---- what it must not do ----

    [Fact]
    public async Task The_landing_page_says_nothing_about_any_shop()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Secret Landing Garage");
        await TestData.SeedJobAsync(factory, shop, plate: "LEAK 777", customerName: "Leaky Customer", phone: "+201000099999");

        var html = await GetAsync(factory.CreateClient(NoRedirect), "/");

        foreach (var secret in new[] { "Secret Landing Garage", "Leaky Customer", "LEAK", "01000099999", "+201000099999", "Cairo" })
        {
            Assert.DoesNotContain(secret, html);
        }
        Assert.DoesNotContain("/shop/logo", html);
        Assert.DoesNotContain("data-chart", html);
        Assert.DoesNotContain("href=\"/Customers", html);
    }

    [Fact]
    public async Task A_signed_in_owner_still_gets_the_dashboard_at_the_same_address()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Dash Garage");

        var html = await GetAsync(factory.CreateClientFor(userId, shop), "/");

        Assert.DoesNotContain("landing-hero", html);
        Assert.Contains("Dash Garage", html);
        Assert.Contains("Open job cards", html);
    }

    [Fact]
    public async Task The_landing_page_stays_on_the_anonymous_allow_list_with_no_data_access()
    {
        // "/" is allowed anonymously on purpose (RolesAndPoliciesTests keeps that list); a technician's signed-in
        // visit is redirected away and a deactivated user is refused: the dashboard guards itself.
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Guarded Home");
        var technician = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);
        var gone = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier, active: false);

        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateClientFor(technician, shop).GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(gone, shop).GetAsync("/")).StatusCode);
    }

    [GeneratedRegex("<img src=\"(/img/landing/[^\"?]+)")]
    private static partial Regex LandingImage();

    [GeneratedRegex("href=\"(https://wa\\.me/[^\"]+)\"")]
    private static partial Regex WhatsAppLinkOnPage();
}
