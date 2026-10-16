using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Html;

namespace GarageDesk.Web.Services;

/// <summary>
/// How a licence plate is drawn in a chip. Egyptian plates read letters first and then the number, but people type
/// them either way round ("4821 س ن م", "9251قسن"). The text direction of the chip then decided what came out: with
/// an Arabic first letter the whole chip turned right-to-left and the number landed on the wrong side of the letters.
///
/// So a plate that has Arabic letters is split into its letters and its digits, each isolated in its own direction
/// (letters right-to-left, digits left-to-right), and always drawn letters first in the reading direction of the page:
/// on the right in Arabic, on the left in English. The stored plate and its search key are never changed; this only
/// changes how the chip looks. Anything that is not exactly "one group of letters and one group of digits" (a Latin
/// plate, an unusual format) is shown as typed.
/// </summary>
public static class PlateFormat
{
    // Same character ranges as the app's web encoder, so Arabic letters are written as letters, not as &#x...; entities.
    private static readonly HtmlEncoder Encoder =
        HtmlEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.Arabic);

    public static IHtmlContent Html(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return HtmlString.Empty;
        }

        var text = plate.Trim();
        var encoder = Encoder;
        if (!text.Any(IsArabicLetter))
        {
            return new HtmlString(encoder.Encode(text));
        }

        if (!TrySplit(text, out var letters, out var digits))
        {
            return new HtmlString("<bdi dir=\"auto\">" + encoder.Encode(text) + "</bdi>");
        }

        return new HtmlString(
            "<bdi class=\"plate-letters\" dir=\"rtl\">" + encoder.Encode(letters) + "</bdi>" +
            "<bdi class=\"plate-digits\" dir=\"ltr\">" + encoder.Encode(digits) + "</bdi>");
    }

    /// <summary>Splits "9251قسن" or "س ن م 4821" into letters and digits. False when it is not exactly one letter group and one digit group.</summary>
    public static bool TrySplit(string plate, out string letters, out string digits)
    {
        var letterBuilder = new StringBuilder();
        var digitBuilder = new StringBuilder();
        var letterGroups = 0;
        var digitGroups = 0;
        var last = '\0'; // the kind of the last letter or digit seen: 'L' or 'D' (separators do not change it)
        var gap = false; // a space or dash came after it

        foreach (var ch in plate)
        {
            if (char.IsLetter(ch))
            {
                // Letters separated only by spaces ("س ن م") are one group; letters that come after digits start a new one.
                if (last != 'L') { letterGroups++; }
                if (gap && letterBuilder.Length > 0) { letterBuilder.Append(' '); }
                letterBuilder.Append(ch);
                last = 'L';
                gap = false;
            }
            else if (char.IsDigit(ch))
            {
                // Digits separated by a space are two groups ("12 34"), which this simple format does not cover.
                if (last != 'D' || gap) { digitGroups++; }
                digitBuilder.Append(ch);
                last = 'D';
                gap = false;
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '/')
            {
                gap = true;
            }
            else
            {
                letters = digits = "";
                return false;
            }
        }

        letters = letterBuilder.ToString();
        digits = digitBuilder.ToString();
        return letterGroups == 1 && digitGroups == 1 && letters.Length > 0 && digits.Length > 0;
    }

    private static bool IsArabicLetter(char c) => c is >= '؀' and <= 'ۿ' && char.IsLetter(c);
}
