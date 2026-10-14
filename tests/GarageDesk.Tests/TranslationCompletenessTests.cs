using System.Globalization;
using System.Text.RegularExpressions;
using GarageDesk.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Tests;

/// <summary>
/// Keeps Arabic from silently falling behind: every English string the app asks the translator for
/// (found by scanning the source) must have an entry in SharedResource.ar.resx.
/// </summary>
public partial class TranslationCompletenessTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Fact]
    public void Every_string_the_app_translates_has_an_arabic_translation()
    {
        var keys = FindTranslatedStrings();
        Assert.True(keys.Count > 50, $"Expected to find the app's strings, found only {keys.Count}.");

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar");
            using var scope = factory.Services.CreateScope();
            var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();

            var missing = keys.Where(key => localizer[key].ResourceNotFound).OrderBy(k => k).ToList();

            Assert.True(missing.Count == 0,
                "Missing Arabic translations in Resources/SharedResource.ar.resx:\n  " + string.Join("\n  ", missing));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static SortedSet<string> FindTranslatedStrings()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "GarageDesk.sln")))
        {
            root = Path.GetDirectoryName(root);
        }
        Assert.NotNull(root);

        var webProject = Path.Combine(root!, "src", "GarageDesk.Web");
        var files = Directory.EnumerateFiles(webProject, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cshtml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var keys = new SortedSet<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in TranslatedString().Matches(text))
            {
                keys.Add(match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
            }
        }
        return keys;
    }

    // L["..."], localizer["..."], RejectAsync("..."), [Display(Name = "...")], Localized(nameof(X), "..."),
    // and the default validation messages in LocalizedValidation ("XAttribute => ...").
    [GeneratedRegex(@"(?:\bL\[|\blocalizer\[|\bRejectAsync\(|Display\(Name = |Localized\(nameof\(\w+\), |Attribute => )""((?:[^""\\]|\\.)*)""")]
    private static partial Regex TranslatedString();
}
