using System.Text;

namespace GarageDesk.Web.Services;

public static class TextNormalization
{
    /// <summary>
    /// Turns Arabic-Indic (٠١٢٣٤٥٦٧٨٩) and Persian (۰۱۲۳۴۵۶۷۸۹) digits into 0-9, so a shop typing on an
    /// Arabic keyboard gets the same phone numbers and plates as one typing on an English keyboard.
    /// </summary>
    public static string ToAsciiDigits(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            result.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch,
            });
        }
        return result.ToString();
    }
}
