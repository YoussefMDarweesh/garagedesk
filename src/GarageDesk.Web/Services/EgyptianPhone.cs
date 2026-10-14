using System.Text.RegularExpressions;

namespace GarageDesk.Web.Services;

/// <summary>
/// Egyptian phone numbers. Whatever the shop types (01012345678, 0101 234 5678, +20 101 234 5678,
/// 0020..., Arabic digits) is stored in one form: "+20" followed by the national number,
/// e.g. +201012345678. That makes searching and, later, WhatsApp links reliable.
/// </summary>
public static partial class EgyptianPhone
{
    // National number = the number without the country code and without the leading trunk 0.
    // Mobile: 10, 11, 12, 15 + 8 digits.  Cairo: 2 + 8.  Alexandria: 3 + 7.  Other governorates: 2-digit code + 7.
    [GeneratedRegex(@"^(1[0125]\d{8}|2\d{8}|3\d{7}|(13|[4-9]\d)\d{7})$")]
    private static partial Regex NationalNumberPattern();

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = TextNormalization.ToAsciiDigits(input.Trim());

        // Only digits and common separators are allowed; a "+" only at the very start.
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var allowed = char.IsAsciiDigit(ch) || ch is ' ' or '-' or '.' or '(' or ')' || (ch == '+' && i == 0);
            if (!allowed)
            {
                return false;
            }
        }

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        string national;

        if (text.StartsWith('+'))
        {
            if (!digits.StartsWith("20"))
            {
                return false; // some other country
            }
            national = digits[2..];
        }
        else if (digits.StartsWith("0020"))
        {
            national = digits[4..];
        }
        else if (digits.StartsWith("20") && digits.Length >= 10)
        {
            national = digits[2..]; // 201012345678: country code without the +
        }
        else
        {
            national = digits;
        }

        // Drop the trunk 0 (01012345678 -> 1012345678), also when typed after +20 (+2001012345678).
        if (national.StartsWith('0'))
        {
            national = national[1..];
        }

        if (!NationalNumberPattern().IsMatch(national))
        {
            return false;
        }

        normalized = "+20" + national;
        return true;
    }

    /// <summary>
    /// Turns what someone typed in the search box into digits to look for inside stored phone numbers,
    /// or null if it is too short to be a useful phone search (so "12" doesn't match half the shop).
    /// </summary>
    public static string? SearchDigits(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var text = TextNormalization.ToAsciiDigits(query).Trim();
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());

        if (text.StartsWith('+') && digits.StartsWith("20"))
        {
            digits = digits[2..]; // an explicit +20 is always the country code, however short the rest is
        }
        else if (digits.StartsWith("0020"))
        {
            digits = digits[4..];
        }
        else if (digits.StartsWith("20") && digits.Length >= 10)
        {
            digits = digits[2..];
        }

        if (digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        return digits.Length >= 3 ? digits : null;
    }
}
