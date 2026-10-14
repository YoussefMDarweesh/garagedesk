namespace GarageDesk.Web.Services;

/// <summary>One entry of the main navigation (sidebar and mobile tab bar).</summary>
/// <param name="Page">The Razor page it links to, e.g. "/Customers/Index".</param>
/// <param name="Prefix">A current page starting with this makes the entry "active".</param>
/// <param name="Label">Already translated.</param>
/// <param name="Icon">A name from the _Icon partial.</param>
/// <param name="Badge">A count to show on the entry (0 = none).</param>
public sealed record NavItem(string Page, string Prefix, string Label, string Icon, int Badge = 0);

/// <summary>Everything the app shell partials need, worked out once in the layout.</summary>
public sealed record ShellModel(
    string CurrentPage,
    string Section,
    IReadOnlyList<NavItem> Items,
    int DueCount,
    string? ShopName,
    string? LogoVersion,
    string? UserName,
    string ReturnUrl);
