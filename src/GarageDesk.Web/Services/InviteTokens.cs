using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace GarageDesk.Web.Services;

/// <summary>Invitation tokens: 256 random bits in the link, only the SHA-256 hash in the database.</summary>
public static class InviteTokens
{
    /// <summary>A new token (URL-safe, 43 characters) and the hash to store.</summary>
    public static (string Token, string Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A cheap shape check so garbage in the URL never reaches the database.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
