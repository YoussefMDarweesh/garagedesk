using System.Globalization;

namespace GarageDesk.Web.Services;

/// <summary>Initials and a stable colour for the round avatars next to customer names.</summary>
public static class Avatars
{
    /// <summary>The first letters of the first two words: "Ahmed Hassan" gives "AH". Works for Arabic names too.</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => First(parts[0]),
            _ => First(parts[0]) + First(parts[1]),
        };

        static string First(string word)
        {
            // Arabic names are often "...the-something": use the letter after the article "ال" (السيد gives س).
            if (word.Length > 2 && word.StartsWith("ال", StringComparison.Ordinal))
            {
                word = word[2..];
            }

            return char.ToUpper(word[0], CultureInfo.InvariantCulture).ToString();
        }
    }

    /// <summary>A hue (0-359) that is always the same for the same name, so a customer keeps their colour.</summary>
    public static int Hue(string? name)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in name ?? "")
            {
                hash = hash * 31 + ch;
            }
            return Math.Abs(hash) % 360;
        }
    }
}
