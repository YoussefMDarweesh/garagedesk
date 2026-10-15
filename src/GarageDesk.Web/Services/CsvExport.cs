using System.Globalization;
using System.Text;

namespace GarageDesk.Web.Services;

/// <summary>
/// Writing CSV by hand (no package). UTF-8 with a byte order mark, so Excel opens Arabic text correctly; CRLF line
/// ends; a cell is quoted only when it has to be. Text that could be read by a spreadsheet as a formula is defused.
/// </summary>
public static class CsvExport
{
    /// <summary>
    /// One text cell. A value that starts with = + - @ (or a tab or carriage return) would be run as a formula by Excel
    /// or Sheets, so it gets a single quote in front: =HYPERLINK(...) is shown as text, not run.
    /// </summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny(['"', ',', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    /// <summary>A money or quantity cell: always two decimals with a dot, never a formula, never locale dependent.</summary>
    public static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The whole file: BOM, the header line, then the rows. Cells must already be made with <see cref="Text"/> or <see cref="Number"/>.</summary>
    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', headers.Select(Text))).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(string.Join(',', row)).Append("\r\n");
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(builder.ToString())];
    }
}
