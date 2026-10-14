using System.Net;
using GarageDesk.Web.Models;
using GarageDesk.Web.Pages.Vehicles;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public class AccentAndAvatarTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---- the shop's accent colour ----

    [Fact]
    public async Task A_shop_can_choose_an_accent_colour_and_the_page_uses_it()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Accent Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Accent", ("accent", "blue"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var db = factory.CreateContextFor(shopId))
        {
            Assert.Equal("blue", (await db.Shops.SingleAsync()).AccentColor);
        }

        // The login cookie will carry the preset, and the page then sets it on <html>.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        var principal = await claimsFactory.CreateAsync((await users.FindByIdAsync(userId))!);
        Assert.Equal("blue", principal.FindFirst(CurrentShop.AccentClaimType)?.Value);

        var withAccent = factory.CreateClientFor(userId, shopId);
        withAccent.DefaultRequestHeaders.Add(TestAuthHandler.ShopAccentHeader, "blue");
        var html = await TestData.BodyAsync(await withAccent.GetAsync("/"));
        Assert.Contains("data-accent=\"blue\"", html);
    }

    [Fact]
    public async Task The_default_accent_is_stored_as_nothing_and_unknown_values_are_refused()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Default Accent Garage");
        var client = factory.CreateClientFor(userId, shopId);
        await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Accent", ("accent", "green"));

        // Back to orange: no value is stored.
        await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Accent", ("accent", "orange"));
        using (var db = factory.CreateContextFor(shopId))
        {
            Assert.Null((await db.Shops.SingleAsync()).AccentColor);
        }

        // Something that is not a preset (e.g. an attempt to inject CSS or markup) changes nothing.
        await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Accent", ("accent", "red\" onload=\"alert(1)"));
        await TestData.SubmitFormAsync(client, "/ShopSettings?handler=Accent", ("accent", "#ff00ff"));
        using var check = factory.CreateContextFor(shopId);
        Assert.Null((await check.Shops.SingleAsync()).AccentColor);
    }

    [Fact]
    public async Task An_unknown_accent_value_in_the_cookie_is_never_written_into_the_page()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Hostile Accent Garage");
        var client = factory.CreateClientFor(userId, shopId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopAccentHeader, "x\" onmouseover=\"alert(1)");

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.DoesNotContain("data-accent", html);
        Assert.DoesNotContain("onmouseover", html);
    }

    [Fact]
    public async Task One_shops_accent_does_not_change_another_shops_settings()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "Accent A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Accent B");
        await TestData.SubmitFormAsync(factory.CreateClientFor(userA, shopA), "/ShopSettings?handler=Accent", ("accent", "purple"));

        using var dbB = factory.CreateContextFor(shopB);
        Assert.Null((await dbB.Shops.SingleAsync()).AccentColor);
        using var dbA = factory.CreateContextFor(shopA);
        Assert.Equal("purple", (await dbA.Shops.SingleAsync()).AccentColor);
    }

    // ---- avatars ----

    [Theory]
    [InlineData("Ahmed Hassan", "AH")]
    [InlineData("ahmed hassan", "AH")]
    [InlineData("Karim", "K")]
    [InlineData("  Nour   El-Din  ", "NE")]
    [InlineData("منى السيد", "مس")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Avatars_show_the_first_letters_of_the_first_two_words(string? name, string expected)
    {
        Assert.Equal(expected, Avatars.Initials(name));
    }

    [Fact]
    public void An_avatar_colour_is_stable_and_within_range()
    {
        foreach (var name in new[] { "Ahmed Hassan", "منى السيد", "", "x" })
        {
            var hue = Avatars.Hue(name);
            Assert.InRange(hue, 0, 359);
            Assert.Equal(hue, Avatars.Hue(name));
        }

        Assert.NotEqual(Avatars.Hue("Ahmed Hassan"), Avatars.Hue("Omar Fathy"));
    }

    // ---- the progress bars ----

    [Theory]
    [InlineData(0, 10_000, 0, "")]
    [InlineData(5_000, 10_000, 50, "")]
    [InlineData(7_999, 10_000, 80, "")]          // rounds to 80 but is still just under the warning line
    [InlineData(8_000, 10_000, 80, "warn")]
    [InlineData(9_999, 10_000, 100, "warn")]
    [InlineData(10_000, 10_000, 100, "bad")]
    [InlineData(25_000, 10_000, 100, "bad")]      // the bar never overflows
    public void Progress_bars_warn_at_eighty_percent_and_turn_red_when_the_interval_is_used_up(
        int value, int interval, int percent, string tone)
    {
        var meter = DetailsModel.Meter(value, interval);

        Assert.Equal(percent, meter.Percent);
        Assert.Equal(tone, meter.Tone);
    }
}
