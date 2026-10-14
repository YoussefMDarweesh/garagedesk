using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public class ShopSetupTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Fact]
    public async Task Signed_in_user_without_a_shop_is_sent_to_create_one()
    {
        var userId = await CreateUserAsync();
        var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/CreateShop", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Create_shop_page_is_reachable_without_a_shop()
    {
        var userId = await CreateUserAsync();
        var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync("/CreateShop");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_visitor_cannot_open_create_shop()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/CreateShop");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Posting_the_form_creates_the_shop_and_links_it_to_the_user()
    {
        var userId = await CreateUserAsync();
        var client = factory.CreateClientFor(userId);

        var response = await PostCreateShopAsync(client, name: "Speedy Garage", phone: "01012345678", address: "12 Nile St, Cairo");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.NotNull(user.ShopId);

        // This scope has no signed-in user, so the filter would hide every shop: look past it.
        var shop = await db.Shops.IgnoreQueryFilters().SingleAsync(s => s.Id == user.ShopId);
        Assert.Equal("Speedy Garage", shop.Name);
        Assert.Equal("01012345678", shop.Phone);
    }

    [Fact]
    public async Task Posting_an_empty_name_shows_the_form_again_and_creates_nothing()
    {
        var userId = await CreateUserAsync();
        var client = factory.CreateClientFor(userId);

        var response = await PostCreateShopAsync(client, name: "", phone: "01012345678", address: "Cairo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Null(user.ShopId);
    }

    [Fact]
    public async Task User_with_a_shop_skips_create_shop_and_sees_their_shop_on_the_home_page()
    {
        var (userId, shopId) = await CreateUserWithShopAsync("Lucky Motors");
        var client = factory.CreateClientFor(userId, shopId);

        var createPage = await client.GetAsync("/CreateShop");
        var home = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, createPage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Lucky Motors", await home.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Home_page_shows_only_the_signed_in_users_own_shop()
    {
        var (userA, shopA) = await CreateUserWithShopAsync("Alpha Auto");
        await CreateUserWithShopAsync("Bravo Auto");
        var client = factory.CreateClientFor(userA, shopA);

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("Alpha Auto", html);
        Assert.DoesNotContain("Bravo Auto", html);
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> PostCreateShopAsync(
        HttpClient client, string name, string phone, string address)
    {
        // Razor Pages protect forms with an anti-forgery token, so fetch the form first like a browser does.
        var form = await client.GetStringAsync("/CreateShop");
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        return await client.PostAsync("/CreateShop", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Phone"] = phone,
            ["Input.Address"] = address,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private async Task<string> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AppUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user);
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private async Task<(string UserId, int ShopId)> CreateUserWithShopAsync(string shopName)
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
        var result = await users.CreateAsync(user);
        Assert.True(result.Succeeded);
        return (user.Id, user.ShopId!.Value);
    }
}
