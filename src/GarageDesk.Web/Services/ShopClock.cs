namespace GarageDesk.Web.Services;

/// <summary>"Today" as the shop sees it. The shops are in Egypt, so the date changes at midnight in Cairo, not in UTC.</summary>
public static class ShopClock
{
    private static readonly TimeZoneInfo Cairo = FindCairo();

    public static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Cairo));

    private static TimeZoneInfo FindCairo()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.Utc;
    }
}
