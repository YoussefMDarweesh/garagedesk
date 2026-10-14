using System.Globalization;

namespace GarageDesk.Web.Services;

public static class MoneyFormat
{
    /// <summary>1250 becomes "1,250.00". The currency (EGP) is added by the page, so it can be translated.</summary>
    public static string Amount(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
