using GarageDesk.Web.Models;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Services;

/// <summary>
/// Builds the grouped navigation for one role. This is presentation only: it decides which links to SHOW, and it
/// mirrors the page policies (Owner / Staff / Team) exactly so that a link is never offered to someone who would get
/// a 403. The policies on the pages remain the real gate; a test checks that every link built here is one the role
/// may open.
///
/// Information architecture (why these groups):
///   Overview  the dashboard, the one page everybody starts from
///   Workshop  the day-to-day work: job cards, estimates, customers, vehicles, reminders
///   Money     who owes me (front desk), then the owner's money pages: profit, expenses, cash close, categories, export, commissions
///   Stock     the parts catalog and the running-low list
///   Admin     the owner's set-up pages: team, import, shop settings
/// A technician sees one section with one entry (their jobs).
/// </summary>
public static class NavBuilder
{
    /// <summary>Entries that stay on the phone's bottom bar; everything else is in the "More" sheet.</summary>
    public const int PhoneTabs = 4;

    private sealed record Section(string Key, string Label, string Icon, List<NavItem> Items, bool Collapsible, bool DefaultOpen);

    public static IReadOnlyList<NavGroup> Build(ShopRole role, string currentPage, string? show, int dueCount, IStringLocalizer<SharedResource> localizer)
    {
        static NavItem Item(string page, string prefix, string label, string icon, int badge = 0, string? showValue = null, bool pinned = false) =>
            new(page, prefix, label, icon, badge, showValue is null ? null : new Dictionary<string, string> { ["show"] = showValue }, Active: false, Pinned: pinned);

        var sections = new List<Section>();

        if (role == ShopRole.Technician)
        {
            // A technician's whole world: the jobs assigned to them. No money, no customer list, no reminders.
            sections.Add(new("workshop", localizer["Workshop"].Value, "tool",
                [Item("/Work/Index", "/Work", localizer["My jobs"].Value, "clipboard", pinned: true)], false, true));
        }
        else
        {
            var owner = role == ShopRole.Owner;

            sections.Add(new("overview", localizer["Overview"].Value, "grid",
                [Item("/Index", "/Index", localizer["Dashboard"].Value, "grid", pinned: true)], false, true));

            sections.Add(new("workshop", localizer["Workshop"].Value, "tool",
            [
                Item("/Jobs/Index", "/Jobs", localizer["Job cards"].Value, "clipboard", pinned: true),
                Item("/Estimates/Index", "/Estimates", localizer["Estimates"].Value, "file-text"),
                Item("/Customers/Index", "/Customers", localizer["Customers"].Value, "users", pinned: true),
                Item("/Vehicles/Index", "/Vehicles", localizer["Vehicles"].Value, "car"),
                Item("/Reminders/Index", "/Reminders", localizer["Reminders"].Value, "bell", dueCount, pinned: true),
            ], true, true));

            var money = new List<NavItem> { Item("/Receivables/Index", "/Receivables", localizer["Who owes me"].Value, "coins") };
            if (owner)
            {
                money.Add(Item("/Money/Profit", "/Money/Profit", localizer["Profit"].Value, "trending-up"));
                money.Add(Item("/Money/Expenses", "/Money/Expenses", localizer["Expenses"].Value, "receipt"));
                money.Add(Item("/Money/CashClose", "/Money/CashClose", localizer["Cash close"].Value, "calculator"));
                money.Add(Item("/Money/Categories", "/Money/Categories", localizer["Expense categories"].Value, "tag"));
                money.Add(Item("/Money/Export", "/Money/Export", localizer["Export"].Value, "download"));
                money.Add(Item("/Reports/Commissions", "/Reports", localizer["Commissions"].Value, "percent"));
            }
            sections.Add(new("money", localizer["Money"].Value, "wallet", money, true, false));

            sections.Add(new("stock", localizer["Stock"].Value, "layers",
            [
                Item("/Parts/Index", "/Parts", localizer["Parts"].Value, "package"),
                Item("/Parts/Index", "/Parts", localizer["Low stock"].Value, "alert", showValue: "low"),
            ], true, false));

            if (owner)
            {
                sections.Add(new("admin", localizer["Admin"].Value, "sliders",
                [
                    Item("/Staff/Index", "/Staff", localizer["My team"].Value, "users"),
                    Item("/Import", "/Import", localizer["Import"].Value, "upload"),
                    Item("/ShopSettings", "/ShopSettings", localizer["Shop settings"].Value, "sliders"),
                ], true, false));
            }
        }

        // Which single entry is "here". "Low stock" and "Parts" share a page: the show=low query tells them apart.
        var onPartsList = currentPage.StartsWith("/Parts/Index", StringComparison.Ordinal);
        var onLowStock = onPartsList && string.Equals(show, "low", StringComparison.OrdinalIgnoreCase);

        bool Matches(NavItem item)
        {
            if (!currentPage.StartsWith(item.Prefix, StringComparison.Ordinal)) { return false; }
            if (item.Page == "/Index") { return currentPage == "/Index"; }
            if (item.Prefix == "/Parts")
            {
                var isLowEntry = item.Route is not null;
                return onLowStock ? isLowEntry : !isLowEntry;
            }
            return true;
        }

        var totalItems = sections.Sum(s => s.Items.Count);
        return sections
            .Select(s => new NavGroup(
                s.Key, s.Label, s.Icon,
                s.Items.Select(i => i with { Active = Matches(i) }).ToList(),
                s.Collapsible,
                // A small menu (a cashier's) opens every section; a big one (the owner's) opens only the workshop to start with.
                s.DefaultOpen || totalItems <= 10))
            .ToList();
    }

    /// <summary>The phone bar: the few pinned entries. It stays the same on every page, so it never jumps around.</summary>
    public static IReadOnlyList<NavItem> PhoneBar(IReadOnlyList<NavGroup> groups) =>
        groups.SelectMany(g => g.Items).Where(i => i.Pinned).Take(PhoneTabs).ToList();

    /// <summary>The "More" sheet: the same sections, minus the entries that are already on the phone bar.</summary>
    public static IReadOnlyList<NavGroup> PhoneMore(IReadOnlyList<NavGroup> groups)
    {
        var bar = PhoneBar(groups).Select(i => (i.Page, i.Label)).ToHashSet();
        return groups
            .Select(g => g with { Items = g.Items.Where(i => !bar.Contains((i.Page, i.Label))).ToList() })
            .Where(g => g.Items.Count > 0)
            .ToList();
    }
}
