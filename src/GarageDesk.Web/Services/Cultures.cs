namespace GarageDesk.Web.Services;

public static class Cultures
{
    public const string Default = "en";

    public static readonly string[] Supported = ["en", "ar"];

    public static bool IsRightToLeft(string languageCode) => languageCode == "ar";
}
