namespace GarageDesk.Web.Services;

/// <summary>
/// The "talk to us on WhatsApp" button of the public landing page. The number is a setting
/// (<c>Landing:WhatsAppNumber</c>, or the <c>Landing__WhatsAppNumber</c> environment variable); empty means no button.
/// It is the product owner's own contact number, never a shop's: the landing page shows nothing about any shop.
/// </summary>
public static class LandingContact
{
    public const string ConfigKey = "Landing:WhatsAppNumber";

    /// <summary>The wa.me link with a pre-filled message, or null when no usable number is configured (the button is then hidden).</summary>
    public static string? WhatsAppUrl(string? configuredNumber, string message)
    {
        if (string.IsNullOrWhiteSpace(configuredNumber))
        {
            return null;
        }

        // "0101 234 5678" and "+20 101 234 5678" both become +201012345678; another country's number is used as typed.
        var phone = EgyptianPhone.TryNormalize(configuredNumber, out var normalized) ? normalized : configuredNumber.Trim();
        return WhatsAppLink.Build(phone, message);
    }
}
