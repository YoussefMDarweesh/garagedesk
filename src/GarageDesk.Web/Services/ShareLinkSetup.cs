using System.Globalization;
using System.Threading.RateLimiting;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GarageDesk.Web.Services;

/// <summary>Wires up the public share pages: services, the per-visitor rate limit and the response headers.</summary>
public static class ShareLinkSetup
{
    public const string RateLimitPolicy = "share-links";

    /// <summary>The public pages live under this path (/s/{token}).</summary>
    public const string PublicPath = "/s";

    public static IServiceCollection AddShareLinks(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShareLinkOptions>(configuration.GetSection("ShareLinks"));
        services.AddScoped<ShareLinkService>();
        services.AddScoped<PublicShareLoader>();

        // The rate limiter that ships with ASP.NET Core (no package). One fixed window per visitor address.
        // Behind Render's proxy the address is the real visitor's, because forwarded headers are enabled there.
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, httpContext =>
            {
                var permits = httpContext.RequestServices.GetRequiredService<IOptions<ShareLinkOptions>>().Value.PermitPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permits),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Call after UseRouting. Adds the privacy headers to everything under /s (including 404 and 429 answers),
    /// then switches the rate limiter on.
    /// </summary>
    public static IApplicationBuilder UseShareLinkProtections(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PublicPath))
            {
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    // Per-customer content behind a secret URL: never stored by a shared cache, never indexed,
                    // and the URL (which holds the secret) is never sent on as a Referer.
                    headers.CacheControl = "private, no-store";
                    headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["X-Content-Type-Options"] = "nosniff";
                    return Task.CompletedTask;
                });
            }

            await next();
        });

        app.UseRateLimiter();

        // After the rate limiter (so floods do not reach the database). Looks the token up once, and sets the page
        // language for the whole request: the visitor's own choice (language cookie) wins, otherwise the language
        // the shop sent the link in. It has to be here: a culture set inside the page handler would be gone
        // by the time the view renders.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PublicPath)
                && context.GetRouteValue("token") is string token
                && context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            {
                var loader = context.RequestServices.GetRequiredService<PublicShareLoader>();
                var link = await loader.FindLiveLinkAsync(token, context.RequestAborted);
                if (link is not null)
                {
                    context.Items[ResolvedLinkKey] = link;
                    if (!context.Request.Cookies.ContainsKey(CookieRequestCultureProvider.DefaultCookieName)
                        && Cultures.Supported.Contains(link.Language))
                    {
                        CultureInfo.CurrentUICulture = new CultureInfo(link.Language);
                    }
                }
            }

            await next();
        });

        return app;
    }

    private const string ResolvedLinkKey = "GarageDesk.ResolvedShareLink";

    /// <summary>The live link the middleware already found for this request, if any.</summary>
    public static ShareLink? ResolvedLink(HttpContext context) =>
        context.Items.TryGetValue(ResolvedLinkKey, out var link) ? link as ShareLink : null;
}
