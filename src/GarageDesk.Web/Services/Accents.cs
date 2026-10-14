namespace GarageDesk.Web.Services;

/// <summary>The accent colours a shop can choose from. "orange" is the default and is never stored.</summary>
public static class Accents
{
    public const string Default = "orange";

    public static readonly string[] Presets = ["orange", "blue", "green", "red", "purple", "teal"];

    /// <summary>The swatch colour shown in the picker for each preset.</summary>
    public static string Swatch(string preset) => preset switch
    {
        "blue" => "#3b82f6",
        "green" => "#22c55e",
        "red" => "#ef4444",
        "purple" => "#8b5cf6",
        "teal" => "#14b8a6",
        _ => "#f97316",
    };

    public static bool IsPreset(string? value) => value is not null && Presets.Contains(value);
}
