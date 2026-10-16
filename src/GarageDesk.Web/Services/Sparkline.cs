using System.Globalization;
using System.Text;

namespace GarageDesk.Web.Services;

/// <summary>
/// The tiny trend line on a dashboard card, as SVG path data worked out on the server from real monthly figures
/// (nothing is invented: with fewer than two months there is no line at all). The chart is purely visual; the exact
/// numbers are always printed next to it.
/// </summary>
public sealed record Sparkline(string Line, string Area, double EndX, double EndY, int Points)
{
    public const double Width = 120;
    public const double Height = 36;
    private const double Pad = 4;

    /// <summary>Null when there is nothing honest to draw (under two points).</summary>
    public static Sparkline? From(IReadOnlyList<decimal> values)
    {
        // Under two months, or months with no money at all (a new shop): nothing real to show, so no line.
        if (values.Count < 2 || values.All(v => v == 0))
        {
            return null;
        }

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        double X(int i) => Pad + (Width - 2 * Pad) * i / (values.Count - 1);
        double Y(decimal v) => range == 0
            ? Height / 2
            : Pad + (Height - 2 * Pad) * (1 - (double)((v - min) / range));

        var line = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            line.Append(i == 0 ? 'M' : 'L')
                .Append(X(i).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(Y(values[i]).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
        }

        var last = values.Count - 1;
        var lineText = line.ToString().TrimEnd();
        var area = $"{lineText} L{X(last).ToString("0.##", CultureInfo.InvariantCulture)} {Height.ToString(CultureInfo.InvariantCulture)} L{X(0).ToString("0.##", CultureInfo.InvariantCulture)} {Height.ToString(CultureInfo.InvariantCulture)} Z";
        return new Sparkline(lineText, area, X(last), Y(values[last]), values.Count);
    }
}
