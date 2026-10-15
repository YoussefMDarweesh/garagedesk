using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GarageDesk.Web.Services;

/// <summary>
/// The secret in a share link. 32 random bytes (256 bits) from the operating system's cryptographic generator,
/// written as 43 URL-safe characters. Only the SHA-256 hash of the token is looked up in the database.
/// </summary>
public static partial class ShareTokens
{
    public const int ByteLength = 32;

    /// <summary>43 characters: 32 bytes in unpadded base64url.</summary>
    public const int TextLength = 43;

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>Quick shape check, done before the database is touched. A wrong shape can never be a real token.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: TextLength } && TokenShape().IsMatch(token);

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenShape();
}

/// <summary>Fills the placeholders of a WhatsApp message template. Only the four known names are replaced, in one pass.</summary>
public static partial class ShareMessage
{
    public const int MaxLength = 500;

    public static readonly string[] Placeholders = ["{name}", "{plate}", "{shop}", "{link}"];

    /// <summary>
    /// Single pass over the template: each known placeholder is replaced by its value, and the inserted text is never
    /// scanned again, so a customer called "{link}" cannot cause a second substitution. Unknown braces stay as typed.
    /// </summary>
    public static string Render(string template, string name, string plate, string shop, string link) =>
        PlaceholderPattern().Replace(template, m => m.Groups[1].Value switch
        {
            "name" => name,
            "plate" => plate,
            "shop" => shop,
            "link" => link,
            _ => m.Value,
        });

    /// <summary>A usable template is not too long and still contains the link, otherwise the message would be pointless.</summary>
    public static bool IsAcceptable(string template) =>
        template.Length <= MaxLength && template.Contains("{link}", StringComparison.Ordinal);

    [GeneratedRegex(@"\{(name|plate|shop|link)\}")]
    private static partial Regex PlaceholderPattern();
}

public class ShareLinkOptions
{
    /// <summary>How long a new link works, in days.</summary>
    public int ExpiryDays { get; set; } = 30;

    /// <summary>Public share page requests allowed per visitor address per minute.</summary>
    public int PermitPerMinute { get; set; } = 60;
}
