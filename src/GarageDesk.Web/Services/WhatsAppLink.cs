namespace GarageDesk.Web.Services;

/// <summary>
/// Builds a click-to-chat link: https://wa.me/201012345678?text=... . It just opens WhatsApp (app or web)
/// with the message typed in; the shop reads it, edits it if it wants to, and presses send. No API, no cost.
/// </summary>
public static class WhatsAppLink
{
    /// <summary>Returns the link, or null if the phone isn't a usable international number.</summary>
    public static string? Build(string? phone, string message)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        // wa.me wants the number in international format with digits only: "+201012345678" becomes "201012345678".
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            return null;
        }

        // EscapeDataString percent-encodes everything unsafe, including spaces, newlines, "&", "#" and Arabic.
        return $"https://wa.me/{digits}?text={Uri.EscapeDataString(message)}";
    }
}
