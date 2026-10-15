using System.Threading.RateLimiting;

namespace GarageDesk.Web.Services;

/// <summary>
/// The rate limit on the public invitation page (/Join/{token}), a sibling of the share-link limit (ShareLinkSetup).
/// It uses the same ASP.NET Core limiter (already switched on by UseShareLinkProtections), one fixed window per
/// visitor address, so a guesser cannot hammer the page. It does not touch the share-links policy.
/// </summary>
public static class InviteRateLimit
{
    public const string Policy = "invites";

    /// <summary>Setting: "Invites:PermitPerMinute" (default 20).</summary>
    public const string SettingName = "Invites:PermitPerMinute";

    public static IServiceCollection AddInviteRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = Math.Max(1, configuration.GetValue(SettingName, 20));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(Policy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });

        return services;
    }
}
