using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

/// <summary>One entry of the main navigation (sidebar, phone tab bar and the phone "More" sheet).</summary>
/// <param name="Page">The Razor page it links to, e.g. "/Customers/Index".</param>
/// <param name="Prefix">A current page starting with this makes the entry "active".</param>
/// <param name="Label">Already translated.</param>
/// <param name="Icon">A name from the _Icon partial.</param>
/// <param name="Badge">A count to show on the entry (0 = none).</param>
/// <param name="Route">Optional query-string values for the link, e.g. show=low for the "Low stock" entry.</param>
/// <param name="Active">Set by <see cref="NavBuilder"/> for the one entry that matches the current request.</param>
/// <param name="Pinned">One of the few entries the phone tab bar keeps; everything else goes into its "More" sheet.</param>
public sealed record NavItem(
    string Page,
    string Prefix,
    string Label,
    string Icon,
    int Badge = 0,
    IDictionary<string, string>? Route = null,
    bool Active = false,
    bool Pinned = false);

/// <summary>A labelled section of the navigation ("Workshop", "Money" ...). A section nobody may open is never created.</summary>
/// <param name="Key">Stable machine name, used for the remembered open/closed state in the browser.</param>
/// <param name="Collapsible">The first section (the dashboard) is always open and has no toggle.</param>
/// <param name="DefaultOpen">Open the first time someone sees it (the active section is always open).</param>
public sealed record NavGroup(string Key, string Label, string Icon, IReadOnlyList<NavItem> Items, bool Collapsible = true, bool DefaultOpen = true)
{
    public bool HasActive => Items.Any(i => i.Active);

    public int Badge => Items.Sum(i => i.Badge);
}

/// <summary>Everything the app shell partials need, worked out once in the layout.</summary>
public sealed record ShellModel(
    string CurrentPage,
    string Section,
    IReadOnlyList<NavGroup> Groups,
    int DueCount,
    string? ShopName,
    string? LogoVersion,
    string? UserName,
    string ReturnUrl,
    ShopRole Role = ShopRole.Owner,
    string? GroupLabel = null,
    string? DisplayName = null)
{
    /// <summary>Every entry the signed-in person may open, in menu order.</summary>
    public IEnumerable<NavItem> Items => Groups.SelectMany(g => g.Items);
}
