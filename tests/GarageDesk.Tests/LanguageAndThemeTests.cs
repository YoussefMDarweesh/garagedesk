using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

public partial class LanguageAndThemeTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---- language ----

    [Fact]
    public async Task A_default_request_is_english_and_left_to_right()
    {
        var client = NewClient();

        var html = await GetHtmlAsync(client, "/");

        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html);
        Assert.Contains("bootstrap.min.css", html);
        Assert.DoesNotContain("bootstrap.rtl.min.css", html);
        Assert.Contains("One place for every job, car and customer.", html);
    }

    [Fact]
    public async Task Choosing_arabic_switches_to_right_to_left_with_the_rtl_stylesheet()
    {
        var client = NewClient();
        await SwitchLanguageAsync(client, "ar");

        var html = await GetHtmlAsync(client, "/");

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
        Assert.Contains("bootstrap.rtl.min.css", html);
        Assert.Contains("مكان واحد لكل أمر شغل وسيارة وعميل.", html);
    }

    [Fact]
    public async Task The_choice_is_remembered_on_other_pages_and_can_be_switched_back()
    {
        var client = NewClient();
        await SwitchLanguageAsync(client, "ar");

        var login = await GetHtmlAsync(client, "/Identity/Account/Login");
        Assert.Contains("dir=\"rtl\"", login);
        Assert.Contains("تسجيل الدخول", login);

        await SwitchLanguageAsync(client, "en");

        var home = await GetHtmlAsync(client, "/");
        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", home);
    }

    [Fact]
    public async Task An_unsupported_language_cookie_falls_back_to_english()
    {
        var client = NewClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c%3Dfr%7Cuic%3Dfr");

        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html);
    }

    [Fact]
    public async Task The_switcher_sets_a_culture_cookie_and_returns_to_the_same_page()
    {
        var client = NewClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        var response = await client.PostAsync("/SetCulture", Form(
            ("culture", "ar"), ("returnUrl", "/Privacy"), ("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Privacy", response.Headers.Location!.ToString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith(".AspNetCore.Culture=c%3Dar%7Cuic%3Dar"));
    }

    [Fact]
    public async Task The_switcher_refuses_to_redirect_to_another_site()
    {
        var client = NewClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        var response = await client.PostAsync("/SetCulture", Form(
            ("culture", "ar"), ("returnUrl", "https://evil.example/phish"), ("__RequestVerificationToken", token)));

        Assert.Equal("/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_switcher_ignores_a_language_we_do_not_support()
    {
        var client = NewClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        var response = await client.PostAsync("/SetCulture", Form(
            ("culture", "fr"), ("returnUrl", "/"), ("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
                     && cookies.Any(c => c.StartsWith(".AspNetCore.Culture")));
    }

    [Fact]
    public async Task Validation_messages_are_translated()
    {
        var userId = await CreateUserAsync();
        var client = factory.CreateClientFor(userId); // signed in, no shop yet: "/" would redirect to /CreateShop
        await SwitchLanguageAsync(client, "ar", tokenPage: "/CreateShop");
        var token = await GetAntiforgeryTokenAsync(client, "/CreateShop");

        var response = await client.PostAsync("/CreateShop", Form(
            ("Input.Name", ""), ("Input.Phone", "01012345678"), ("Input.Address", "Cairo"),
            ("__RequestVerificationToken", token)));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("حقل اسم الورشة مطلوب.", html);
    }

    [Fact]
    public async Task Account_errors_from_identity_are_translated()
    {
        var client = NewClient();
        await SwitchLanguageAsync(client, "ar");
        var token = await GetAntiforgeryTokenAsync(client, "/Identity/Account/Register");

        // Long enough to pass the length rule, but with no digit, uppercase letter or symbol.
        var response = await client.PostAsync("/Identity/Account/Register", Form(
            ("Input.Email", $"{Guid.NewGuid():N}@example.com"), ("Input.Password", "abcdefgh"),
            ("Input.ConfirmPassword", "abcdefgh"), ("__RequestVerificationToken", token)));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("يجب أن تحتوي كلمة المرور على رقم واحد على الأقل", html);
    }

    // ---- dark mode ----

    [Fact]
    public async Task Every_page_sets_the_theme_before_paint_and_has_a_toggle()
    {
        var client = NewClient();

        var html = await GetHtmlAsync(client, "/Privacy");

        // The inline script is in <head>, ahead of the stylesheets, so there is no flash of the wrong theme.
        var script = html.IndexOf("localStorage.getItem(\"gd-theme\")", StringComparison.Ordinal);
        var stylesheet = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.True(script > 0 && script < stylesheet, "theme script must come before the stylesheets");
        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.Contains("js-theme-toggle", html);
    }

    // ---- helpers ----

    private HttpClient NewClient() => factory.CreateClient(new() { AllowAutoRedirect = false });

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task SwitchLanguageAsync(HttpClient client, string culture, string tokenPage = "/")
    {
        // Same steps as the navbar buttons: fetch a page for the form token, then post the choice.
        // (Every page has the switcher, so any page the user can open will do.)
        var token = await GetAntiforgeryTokenAsync(client, tokenPage);
        var response = await client.PostAsync("/SetCulture", Form(
            ("culture", culture), ("returnUrl", "/"), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No anti-forgery token found on {path}");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

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

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
