using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace GarageDesk.Tests;

/// <summary>Things that only matter once the app runs in a container behind a platform's proxy (Render).</summary>
public class HostingTests
{
    /// <summary>The real app with the real Npgsql provider, pointed at a port where no database listens.</summary>
    private sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Default",
                "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Command Timeout=2");
    }

    /// <summary>The test app, started the way the Docker image starts it: trusting X-Forwarded-* headers.</summary>
    private sealed class BehindProxyFactory : GarageDeskWebFactory
    {
        private const string Variable = "ASPNETCORE_FORWARDEDHEADERS_ENABLED";

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // ASP.NET reads this one straight from the process environment while the host is being built
            // (a test setting is ignored), so set it for exactly that moment and put it back.
            var before = Environment.GetEnvironmentVariable(Variable);
            Environment.SetEnvironmentVariable(Variable, "true");
            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Variable, before);
            }
        }
    }

    [Fact]
    public async Task Health_check_answers_200_without_a_login_and_without_the_database()
    {
        await using var factory = new UnreachableDatabaseFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Health_check_also_answers_200_when_the_database_works()
    {
        using var factory = new GarageDeskWebFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    // The language switcher marks its cookie Secure only when the app believes the request came in over HTTPS
    // (Request.IsHttps), so that cookie shows exactly what the app sees behind the proxy.
    private static async Task<(HttpResponseMessage Response, string Cookie)> SwitchLanguageAsync(HttpClient client)
    {
        var token = await TestData.TokenAsync(client, "/");
        var response = await client.PostAsync("/SetCulture", new FormUrlEncodedContent(
        [
            new("culture", "ar"), new("returnUrl", "/"), new("__RequestVerificationToken", token),
        ]));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Culture"));
        return (response, cookie);
    }

    [Fact]
    public async Task Behind_the_proxy_a_forwarded_https_request_is_served_without_a_redirect_and_the_app_sees_https()
    {
        await using var factory = new BehindProxyFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        // No redirect loop: the proxy's plain-HTTP hop is served directly.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        var (response, cookie) = await SwitchLanguageAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_the_forwarded_header_the_request_is_plain_http_and_the_cookie_is_not_secure()
    {
        await using var factory = new BehindProxyFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (_, cookie) = await SwitchLanguageAsync(client);

        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
