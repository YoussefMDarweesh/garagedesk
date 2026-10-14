using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>Helpers shared by the HTTP-level tests.</summary>
public static partial class TestData
{
    public static async Task<(string UserId, int ShopId)> CreateOwnerAsync(GarageDeskWebFactory factory, string shopName)
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

    public static async Task<string> CreateUserWithoutShopAsync(GarageDeskWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AppUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        return user.Id;
    }

    /// <summary>Fetches a page and reads its anti-forgery token, like a browser about to submit a form.</summary>
    public static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No anti-forgery token found on {path}");
        return match.Groups[1].Value;
    }

    /// <summary>Submits the form on <paramref name="path"/> the way a browser would (GET for the token, then POST).</summary>
    public static async Task<HttpResponseMessage> SubmitFormAsync(
        HttpClient client, string path, params (string Key, string Value)[] fields)
    {
        var token = await TokenAsync(client, path);
        var content = new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                  .Append(new("__RequestVerificationToken", token)));
        return await client.PostAsync(path, content);
    }

    /// <summary>The response body with HTML entities decoded, so assertions can use plain (including Arabic) text.</summary>
    public static async Task<string> BodyAsync(HttpResponseMessage response) =>
        System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
