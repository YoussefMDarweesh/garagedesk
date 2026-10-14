using System.Net;
using System.Security.Claims;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public class UiPagesTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Fact]
    public async Task Anonymous_home_page_invites_visitors_to_create_a_shop()
    {
        var html = await GetHtmlAsync("/");

        Assert.Contains("One place for every job, car and customer.", html);
        Assert.Contains("Create your shop", html);
        Assert.Contains("href=\"/Identity/Account/Login\"", html);
    }

    [Fact]
    public async Task Login_page_uses_the_restyled_view()
    {
        var html = await GetHtmlAsync("/Identity/Account/Login");

        Assert.Contains("Welcome back to your workshop.", html);
        Assert.Contains("id=\"login-submit\"", html);
        Assert.Contains("name=\"Input.Password\"", html);
    }

    [Fact]
    public async Task Register_page_uses_the_restyled_view()
    {
        var html = await GetHtmlAsync("/Identity/Account/Register");

        Assert.Contains("Create your account", html);
        Assert.Contains("name=\"Input.ConfirmPassword\"", html);
    }

    [Fact]
    public async Task Privacy_page_renders()
    {
        var html = await GetHtmlAsync("/Privacy");

        Assert.Contains("What we store", html);
    }

    [Fact]
    public async Task Navbar_shows_the_shop_name_for_a_signed_in_owner()
    {
        var (userId, shopId) = await CreateUserWithShopAsync("Nile Auto Care");
        var client = factory.CreateClientFor(userId, shopId);
        // Test auth supplies the shop_id claim; add the name claim the real login cookie would carry.
        client.DefaultRequestHeaders.Add(TestAuthHandler.ShopNameHeader, "Nile Auto Care");

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // The app shell: the sidebar carries the shop name, the user menu carries Log out.
        Assert.Contains("class=\"sidebar\"", html);
        Assert.Contains("Nile Auto Care", html);
        Assert.Contains("Log out", html);
    }

    [Fact]
    public async Task Login_cookie_carries_the_shop_id_and_name()
    {
        var (userId, shopId) = await CreateUserWithShopAsync("Cairo Garage");

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        var user = await users.FindByIdAsync(userId);

        var principal = await claimsFactory.CreateAsync(user!);

        Assert.Equal(shopId.ToString(), principal.FindFirstValue(CurrentShop.ClaimType));
        Assert.Equal("Cairo Garage", principal.FindFirstValue(CurrentShop.NameClaimType));
    }

    private async Task<string> GetHtmlAsync(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
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
