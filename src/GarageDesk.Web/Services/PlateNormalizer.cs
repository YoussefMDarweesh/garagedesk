using System.Text;

namespace GarageDesk.Web.Services;

/// <summary>
/// Builds the search key for a licence plate, so "أ ب ج 1234", "ابج-١٢٣٤" and "abc 123" are found
/// however they were typed: letters and digits only, ASCII digits, Latin letters upper-cased,
/// and the Arabic alef variants (أ إ آ) folded to plain ا.
/// </summary>
public static class PlateNormalizer
{
    public static string Key(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return "";
        }

        var result = new StringBuilder(plate.Length);
        foreach (var ch in TextNormalization.ToAsciiDigits(plate))
        {
            if (!char.IsLetterOrDigit(ch))
            {
                continue;
            }

            result.Append(ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                _ => char.ToUpperInvariant(ch),
            });
        }
        return result.ToString();
    }
}
