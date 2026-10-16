using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Tests;

/// <summary>
/// ui-polish-v2: grouped navigation, the phone "More" sheet, the "Today" hero and money cards, licence plate order and
/// the self-hosted fonts. Presentation only: none of these tests change what a role may open (RolesAndPoliciesTests
/// still owns that); they check that the menu never offers a link a role cannot open.
/// </summary>
public class UiPolishV2Tests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static DateOnly Today => ShopClock.Today();

    /// <summary>Returns the key itself, so tests can read menu labels in English without a web host.</summary>
    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static IReadOnlyList<NavGroup> Menu(ShopRole role, string page = "/Index", string? show = null, int due = 0) =>
        NavBuilder.Build(role, page, show, due, new KeyLocalizer());

    private async Task<(HttpClient Client, string UserId, int ShopId)> SignedInAsync(ShopRole role, string? name = null)
    {
        var (ownerId, shopId) = await TestData.CreateOwnerAsync(factory, name ?? $"Polish {role} {Guid.NewGuid():N}");
        var userId = role == ShopRole.Owner ? ownerId : await TestData.CreateStaffAsync(factory, shopId, role);
        return (factory.CreateClientFor(userId, shopId), userId, shopId);
    }

    private static List<string> RailLinks(string html) =>
        Regex.Matches(html, "<a class=\"rail-link[^\"]*\"[^>]*href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

    private static string Section(string html, string startMarker, string endMarker)
    {
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' not found");
        var end = html.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{endMarker}' not found after '{startMarker}'");
        return html[start..end];
    }

    private static List<string> Hrefs(string fragment) =>
        Regex.Matches(fragment, "href=\"([^\"#]+)\"").Select(m => m.Groups[1].Value).ToList();

    // ---------------------------------------------------------------- grouping per role (no web host needed)

    [Fact]
    public void An_owner_sees_every_section_in_a_sensible_order()
    {
        var groups = Menu(ShopRole.Owner);

        Assert.Equal(["overview", "workshop", "money", "stock", "admin"], groups.Select(g => g.Key));
        Assert.Equal(["Dashboard"], groups[0].Items.Select(i => i.Label));
        Assert.Equal(["Job cards", "Estimates", "Customers", "Vehicles", "Reminders"], groups[1].Items.Select(i => i.Label));
        Assert.Equal(["Who owes me", "Profit", "Expenses", "Cash close", "Expense categories", "Export", "Commissions"], groups[2].Items.Select(i => i.Label));
        Assert.Equal(["Parts", "Low stock"], groups[3].Items.Select(i => i.Label));
        Assert.Equal(["My team", "Import", "Shop settings"], groups[4].Items.Select(i => i.Label));
    }

    [Fact]
    public void A_cashier_gets_no_admin_section_and_none_of_the_owners_money_pages()
    {
        var groups = Menu(ShopRole.Cashier);

        Assert.DoesNotContain(groups, g => g.Key == "admin");
        Assert.Equal(["overview", "workshop", "money", "stock"], groups.Select(g => g.Key));
        Assert.Equal(["Who owes me"], groups.Single(g => g.Key == "money").Items.Select(i => i.Label));

        var pages = groups.SelectMany(g => g.Items).Select(i => i.Page).ToList();
        foreach (var ownerOnly in new[] { "/Money/Profit", "/Money/Expenses", "/Money/CashClose", "/Money/Categories", "/Money/Export", "/Reports/Commissions", "/Staff/Index", "/Import", "/ShopSettings" })
        {
            Assert.DoesNotContain(ownerOnly, pages);
        }
    }

    [Fact]
    public void A_technician_sees_one_section_with_their_jobs_and_nothing_else()
    {
        var groups = Menu(ShopRole.Technician, "/Work/Index");

        var only = Assert.Single(groups);
        Assert.Equal("workshop", only.Key);
        var item = Assert.Single(only.Items);
        Assert.Equal("/Work/Index", item.Page);
        Assert.True(item.Active);
        Assert.False(only.Collapsible); // one section with one link needs no toggle
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Cashier)]
    [InlineData(ShopRole.Technician)]
    public void No_section_is_ever_empty(ShopRole role)
    {
        Assert.All(Menu(role), g => Assert.NotEmpty(g.Items));
    }

    [Fact]
    public void The_due_reminders_badge_stays_on_the_reminders_entry_and_on_its_section()
    {
        var workshop = Menu(ShopRole.Owner, due: 8).Single(g => g.Key == "workshop");

        Assert.Equal(8, workshop.Items.Single(i => i.Page == "/Reminders/Index").Badge);
        Assert.Equal(8, workshop.Badge); // shown on the header when the section is folded away
        Assert.Equal(0, workshop.Items.Where(i => i.Page != "/Reminders/Index").Sum(i => i.Badge));
    }

    [Theory]
    [InlineData("/Money/Profit", "money", "/Money/Profit")]
    [InlineData("/Money/CashClose", "money", "/Money/CashClose")]
    [InlineData("/Reports/Commissions", "money", "/Reports/Commissions")]
    [InlineData("/Parts/Edit", "stock", "/Parts/Index")]
    [InlineData("/Staff/Index", "admin", "/Staff/Index")]
    [InlineData("/Import", "admin", "/Import")]
    [InlineData("/Jobs/Details", "workshop", "/Jobs/Index")]
    [InlineData("/Index", "overview", "/Index")]
    public void The_active_page_marks_exactly_one_entry_and_its_section(string currentPage, string group, string activePage)
    {
        var groups = Menu(ShopRole.Owner, currentPage);

        var active = groups.SelectMany(g => g.Items.Where(i => i.Active).Select(i => (Group: g, Item: i))).ToList();
        var hit = Assert.Single(active);
        Assert.Equal(group, hit.Group.Key);
        Assert.Equal(activePage, hit.Item.Page);
        Assert.Equal(group, Assert.Single(groups, g => g.HasActive).Key);
    }

    [Fact]
    public void Low_stock_and_parts_share_a_page_but_only_one_is_active_at_a_time()
    {
        var parts = Menu(ShopRole.Owner, "/Parts/Index").SelectMany(g => g.Items).Where(i => i.Active).ToList();
        Assert.Equal("Parts", Assert.Single(parts).Label);

        var low = Menu(ShopRole.Owner, "/Parts/Index", show: "low").SelectMany(g => g.Items).Where(i => i.Active).ToList();
        Assert.Equal("Low stock", Assert.Single(low).Label);
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Cashier)]
    public void The_phone_bar_and_the_more_sheet_together_hold_every_entry_of_the_rail_exactly_once(ShopRole role)
    {
        var groups = Menu(role);
        var bar = NavBuilder.PhoneBar(groups);
        var more = NavBuilder.PhoneMore(groups).SelectMany(g => g.Items).ToList();

        Assert.InRange(bar.Count, 1, NavBuilder.PhoneTabs);
        var rail = groups.SelectMany(g => g.Items).Select(i => (i.Page, i.Label)).OrderBy(x => x).ToList();
        var phone = bar.Concat(more).Select(i => (i.Page, i.Label)).OrderBy(x => x).ToList();
        Assert.Equal(rail, phone);
        Assert.Equal(phone.Count, phone.Distinct().Count());
    }

    // ---------------------------------------------------------------- the real pages

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Cashier)]
    [InlineData(ShopRole.Technician)]
    public async Task Every_link_in_the_rail_is_one_the_role_can_actually_open(ShopRole role)
    {
        var (client, _, _) = await SignedInAsync(role);
        var home = role == ShopRole.Technician ? "/Work" : "/";

        var links = RailLinks(await TestData.BodyAsync(await client.GetAsync(home))).Distinct().ToList();
        Assert.NotEmpty(links);

        foreach (var link in links)
        {
            var response = await client.GetAsync(link);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} was offered {link} but got {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task The_rail_scrolls_in_the_middle_and_keeps_the_pin_button_outside_the_scrolling_part()
    {
        var (client, _, _) = await SignedInAsync(ShopRole.Owner);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        var scrollStart = html.IndexOf("class=\"rail-scroll\"", StringComparison.Ordinal);
        var scrollEnd = html.IndexOf("</nav>", scrollStart, StringComparison.Ordinal);
        var pin = html.IndexOf("id=\"sidebar-pin\"", StringComparison.Ordinal);
        Assert.True(scrollStart > 0 && pin > scrollEnd, "The pin button must come after (outside) the scrolling navigation.");
        Assert.Contains("aria-label=\"Main navigation\"", html);
    }

    [Fact]
    public async Task The_section_that_holds_the_current_page_opens_itself_and_the_others_keep_their_default()
    {
        var (client, _, _) = await SignedInAsync(ShopRole.Owner);

        var html = await TestData.BodyAsync(await client.GetAsync("/Money/Profit"));

        string GroupClass(string key) => Regex.Match(html, $"class=\"rail-group([^\"]*)\" data-group=\"{key}\"").Groups[1].Value;
        Assert.Contains("is-open", GroupClass("money"));
        Assert.Contains("has-active", GroupClass("money"));
        Assert.Matches("aria-expanded=\"true\" aria-controls=\"rail-money\"", html);

        // The owner's menu is big, so only the workshop starts open; stock and admin are folded away (but still in the page).
        Assert.Contains("is-open", GroupClass("workshop"));
        Assert.DoesNotContain("is-open", GroupClass("stock"));
        Assert.DoesNotContain("is-open", GroupClass("admin"));
        Assert.Matches("aria-expanded=\"false\" aria-controls=\"rail-stock\"", html);
    }

    [Fact]
    public async Task Low_stock_marks_its_own_entry_when_the_list_is_filtered_to_low_stock()
    {
        var (client, _, _) = await SignedInAsync(ShopRole.Owner);

        var low = await TestData.BodyAsync(await client.GetAsync("/Parts?show=low"));
        var all = await TestData.BodyAsync(await client.GetAsync("/Parts"));

        Assert.Equal(["/Parts?show=low"], Regex.Matches(low, "<a class=\"rail-link active\"[^>]*href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList());
        Assert.Equal(["/Parts"], Regex.Matches(all, "<a class=\"rail-link active\"[^>]*href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList());
    }

    [Fact]
    public async Task Sections_nobody_may_open_are_not_in_the_page_at_all()
    {
        var (cashier, _, _) = await SignedInAsync(ShopRole.Cashier);
        var cashierHtml = await TestData.BodyAsync(await cashier.GetAsync("/"));
        Assert.DoesNotContain("data-group=\"admin\"", cashierHtml);
        Assert.Contains("data-group=\"money\"", cashierHtml);
        Assert.DoesNotContain("href=\"/Money/Profit\"", cashierHtml);
        Assert.DoesNotContain("href=\"/Staff\"", cashierHtml);

        var (tech, _, _) = await SignedInAsync(ShopRole.Technician);
        var techHtml = await TestData.BodyAsync(await tech.GetAsync("/Work"));
        foreach (var gone in new[] { "money", "stock", "admin", "overview" })
        {
            Assert.DoesNotContain($"data-group=\"{gone}\"", techHtml);
        }
        Assert.Equal(["/Work"], RailLinks(techHtml));
        Assert.DoesNotContain("rail-group-toggle\" aria-expanded", techHtml); // nothing to fold
        Assert.DoesNotContain("id=\"more-sheet\"", techHtml);
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Cashier)]
    public async Task The_phone_more_sheet_lists_the_same_permitted_entries_as_the_rail(ShopRole role)
    {
        var (client, _, _) = await SignedInAsync(role);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        var rail = RailLinks(html).Select(h => h.TrimEnd('/')).OrderBy(h => h).ToList();
        var tabs = Hrefs(Section(html, "class=\"tabbar\"", "</nav>")).Select(h => h.TrimEnd('/')).ToList();
        // The sheet is the last thing in the shell: every link from its start to the end of the page belongs to it.
        var sheet = Hrefs(html[html.IndexOf("id=\"more-sheet\"", StringComparison.Ordinal)..]).Select(h => h.TrimEnd('/')).ToList();

        Assert.InRange(tabs.Count, 2, 5);
        var phone = tabs.Concat(sheet).OrderBy(h => h).ToList();
        Assert.Equal(rail, phone);
        Assert.Contains("aria-controls=\"more-sheet\"", html);
        Assert.Contains("data-more-close", html);
    }

    [Fact]
    public async Task In_arabic_the_rail_is_on_the_right_because_the_stylesheet_only_uses_start_and_end()
    {
        var (client, _, _) = await SignedInAsync(ShopRole.Owner);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var html = await TestData.BodyAsync(await client.GetAsync("/Money/Profit"));
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\"", html);
        Assert.Contains("الورشة", html);          // the section labels are translated
        Assert.Contains("الإدارة", html);
        Assert.Matches(@"\.sidebar \{[^}]*inset-inline-start: var\(--gutter\);", css);
        Assert.DoesNotMatch(@"\.sidebar \{[^}]*\b(left|right):", css);
    }

    // ---------------------------------------------------------------- the dashboard

    [Fact]
    public async Task The_money_cards_keep_the_currency_on_the_same_line_as_the_number()
    {
        var (client, _, shopId) = await SignedInAsync(ShopRole.Owner);
        await SeedPaidJobAsync(shopId, Today, 700m);

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync("/")), @"\s+", " ");

        foreach (var card in new[] { "revenue", "profit", "outstanding" })
        {
            var markup = Regex.Match(html, $"data-kpi=\"{card}\".*?</a>").Value;
            Assert.Matches(@"<p class=""kpi-value stat-value[^""]*""[^>]*><span class=""ltr"">[\d,\.\-]+</span> <small class=""stat-unit"">EGP</small></p>", markup);
        }
        // ...and the stylesheet keeps them on one line, baseline to baseline.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "css", "site.css"));
        Assert.Matches(@"\.kpi-value \{[^}]*align-items: baseline;[^}]*white-space: nowrap;", css);
    }

    [Fact]
    public async Task The_owners_hero_chips_count_real_data_and_link_to_the_right_pages()
    {
        var (client, _, shopId) = await SignedInAsync(ShopRole.Owner);
        await SeedJobAsync(shopId, JobStatus.InProgress, 100m);
        await SeedJobAsync(shopId, JobStatus.InProgress, 100m);
        await SeedJobAsync(shopId, JobStatus.Done, 250m);
        await SeedJobAsync(shopId, JobStatus.Booked, 999m);
        await SeedOverdueReminderAsync(shopId);

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync("/")), @"\s+", " ");

        (string Href, string Number) Chip(string key)
        {
            var tag = Regex.Match(html, $@"<a [^>]*data-attn=""{key}""[^>]*>(.*?)</a>");
            Assert.True(tag.Success, $"chip {key} missing");
            var href = Regex.Match(tag.Value, @"href=""([^""]+)""").Groups[1].Value;
            var number = Regex.Match(tag.Groups[1].Value, @"<span class=""attn-num[^""]*"">(.*?)</span>(?= <span class=""attn-label"")").Groups[1].Value;
            return (href, Regex.Replace(number, "<[^>]+>", "").Trim());
        }

        var inShop = Chip("in-shop");
        Assert.Equal("/Jobs?status=InProgress", inShop.Href);
        Assert.Equal("2", inShop.Number);

        var ready = Chip("ready");
        Assert.Equal("/Jobs?status=Done", ready.Href);
        Assert.Equal("1", ready.Number);

        var overdue = Chip("overdue");
        Assert.Equal("/Reminders", overdue.Href);
        Assert.Equal("1", overdue.Number);

        var collect = Chip("collect");
        Assert.Equal("/Receivables", collect.Href);
        Assert.Contains("250.00", collect.Number); // the one Done job; the Booked one is not owed yet
        Assert.Contains("Today at", html);
    }

    [Fact]
    public async Task A_brand_new_shops_hero_is_calm_and_invents_no_numbers_or_trend_lines()
    {
        var (client, _, _) = await SignedInAsync(ShopRole.Owner, $"Quiet {Guid.NewGuid():N}");

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync("/")), @"\s+", " ");

        Assert.Contains("Nothing is waiting on you right now.", html);
        Assert.Equal(4, Regex.Matches(html, "class=\"attn attn-calm\"").Count);
        Assert.DoesNotContain("class=\"spark\"", html);          // no months with money: no trend line
        Assert.DoesNotContain("trend-badge", html);              // no last month to compare with
    }

    [Fact]
    public async Task The_trend_badge_and_trend_line_show_when_there_is_something_real_to_compare()
    {
        var (client, _, shopId) = await SignedInAsync(ShopRole.Owner);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        await SeedPaidJobAsync(shopId, thisMonth, 700m);
        await SeedPaidJobAsync(shopId, thisMonth.AddMonths(-1), 300m);

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync("/")), @"\s+", " ");

        var revenue = Regex.Match(html, "data-kpi=\"revenue\".*?</a>").Value;
        Assert.Contains("trend-badge trend-up", revenue);
        Assert.Contains("+133%", revenue);
        Assert.Contains("class=\"spark\"", revenue);
    }

    [Fact]
    public async Task A_cashiers_dashboard_has_the_hero_and_the_outstanding_card_but_no_profit_revenue_or_trend_lines()
    {
        var (client, _, shopId) = await SignedInAsync(ShopRole.Cashier);
        await SeedPaidJobAsync(shopId, new DateOnly(Today.Year, Today.Month, 1), 700m);
        await SeedJobAsync(shopId, JobStatus.Done, 80m);

        var html = Regex.Replace(await TestData.BodyAsync(await client.GetAsync("/")), @"\s+", " ");

        Assert.Contains("data-hero", html);
        Assert.Contains("data-attn=\"collect\"", html);
        Assert.Contains("data-kpi=\"outstanding\"", html);
        Assert.DoesNotContain("data-kpi=\"revenue\"", html);
        Assert.DoesNotContain("data-kpi=\"profit\"", html);
        Assert.DoesNotContain("data-profit", html);
        Assert.DoesNotContain("class=\"spark\"", html);
        Assert.DoesNotContain("data-chart", html);
    }

    // ---------------------------------------------------------------- plates

    [Theory]
    [InlineData("9251قسن", "قسن", "9251")]
    [InlineData("قسن9251", "قسن", "9251")]
    [InlineData("س ن م 4821", "س ن م", "4821")]
    [InlineData("4821 س ن م", "س ن م", "4821")]
    [InlineData("أ ب ج-1234", "أ ب ج", "1234")]
    public void An_arabic_plate_is_split_into_letters_and_digits(string plate, string letters, string digits)
    {
        Assert.True(PlateFormat.TrySplit(plate, out var l, out var d));
        Assert.Equal(letters, l);
        Assert.Equal(digits, d);
    }

    [Theory]
    [InlineData("12 34 س")]       // two digit groups
    [InlineData("س 12 ن 34")]     // letters on both sides of the digits
    [InlineData("س ن م")]         // no digits
    [InlineData("4821")]          // no letters
    [InlineData("س ن م 48#21")]   // something that is not a plate character
    public void Anything_that_is_not_one_group_of_letters_and_one_of_digits_is_left_as_typed(string plate)
    {
        Assert.False(PlateFormat.TrySplit(plate, out _, out _));
        Assert.DoesNotContain("plate-letters", PlateFormat.Html(plate).ToString());
    }

    [Fact]
    public void The_plate_chip_always_draws_letters_then_digits_and_encodes_what_it_is_given()
    {
        Assert.Equal("<bdi class=\"plate-letters\" dir=\"rtl\">قسن</bdi><bdi class=\"plate-digits\" dir=\"ltr\">9251</bdi>", PlateFormat.Html("9251قسن").ToString());
        Assert.Equal("ABC 123", PlateFormat.Html("ABC 123").ToString());          // Latin plates are untouched
        Assert.Equal("", PlateFormat.Html(null).ToString());
        Assert.DoesNotContain("<script", PlateFormat.Html("س <script> 12").ToString());
    }

    [Fact]
    public async Task A_plate_typed_digits_first_is_shown_letters_first_on_the_vehicle_page_in_both_languages()
    {
        var (client, _, shopId) = await SignedInAsync(ShopRole.Owner);
        int vehicleId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var vehicle = new Vehicle
            {
                Customer = new Customer { Name = "Plate Owner", Phone = "+201000000001" },
                Plate = "9251قسن", PlateKey = "9251قسن", Make = "Kia", Model = "Rio", Year = 2020,
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }

        foreach (var culture in new[] { "en", "ar" })
        {
            var token = await TestData.TokenAsync(client, "/Customers");
            await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = culture, ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
            }));

            var html = await TestData.BodyAsync(await client.GetAsync($"/Vehicles/Details/{vehicleId}"));

            var letters = html.IndexOf("<bdi class=\"plate-letters\" dir=\"rtl\">قسن</bdi>", StringComparison.Ordinal);
            var digits = html.IndexOf("<bdi class=\"plate-digits\" dir=\"ltr\">9251</bdi>", StringComparison.Ordinal);
            Assert.True(letters > 0 && digits > letters, $"{culture}: the letters must come before the digits in the markup");
            // the chip itself no longer forces a direction (that is what flipped the order), so it follows the page
            Assert.DoesNotMatch("class=\"plate plate-lg\" dir=", html);
        }
    }

    // ---------------------------------------------------------------- the trend line

    [Fact]
    public void A_trend_line_needs_two_real_points_and_some_money()
    {
        Assert.Null(Sparkline.From([]));
        Assert.Null(Sparkline.From([5m]));
        Assert.Null(Sparkline.From([0m, 0m, 0m]));

        var line = Sparkline.From([10m, 20m, 15m, 30m])!;
        Assert.StartsWith("M", line.Line);
        Assert.Equal(4, line.Points);
        Assert.EndsWith("Z", line.Area);
        Assert.InRange(line.EndX, 0, Sparkline.Width);
        Assert.InRange(line.EndY, 0, Sparkline.Height);
        Assert.True(line.EndY < Sparkline.Height / 2, "the highest value sits in the upper half");

        var flat = Sparkline.From([7m, 7m, 7m])!;
        Assert.Equal(Sparkline.Height / 2, flat.EndY);
    }

    // ---------------------------------------------------------------- fonts

    [Fact]
    public async Task The_self_hosted_fonts_are_served_documented_and_small()
    {
        var client = factory.CreateClient();
        var fontsDir = Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "fonts");

        var files = Directory.GetFiles(fontsDir, "*.woff2");
        Assert.NotEmpty(files);
        Assert.True(files.Sum(f => new FileInfo(f).Length) < 1_500_000, "fonts must stay under about 1.5 MB in total");
        Assert.True(File.Exists(Path.Combine(fontsDir, "NOTICE.txt")));
        Assert.Contains("SIL OPEN FONT LICENSE", File.ReadAllText(Path.Combine(fontsDir, "NOTICE.txt")));

        foreach (var file in files)
        {
            var response = await client.GetAsync("/fonts/" + Path.GetFileName(file));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
        }

        // Every font file the stylesheet asks for exists, and the system fonts remain as the fallback.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GarageDesk.Web", "wwwroot", "css", "site.css"));
        foreach (Match m in Regex.Matches(css, @"url\(""\.\./fonts/([^""]+)""\)"))
        {
            Assert.True(File.Exists(Path.Combine(fontsDir, m.Groups[1].Value)), $"{m.Groups[1].Value} is declared in site.css but missing");
        }
        Assert.Contains("\"Segoe UI\"", css);
        Assert.Contains("system-ui", css);
    }

    // ---------------------------------------------------------------- helpers

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GarageDesk.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private async Task SeedJobAsync(int shopId, JobStatus status, decimal price)
    {
        using var db = factory.CreateContextFor(shopId);
        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = $"Owner {Guid.NewGuid():N}"[..14], Phone = "+201000000000" },
            Plate = $"P{Random.Shared.Next(1000, 9999)}", PlateKey = $"P{Random.Shared.Next(1000, 9999)}", Make = "Kia", Model = "Rio", Year = 2020,
        };
        var job = new JobCard { Vehicle = vehicle, DateIn = Today, Status = status };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Work", Quantity = 1, UnitPrice = price });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
    }

    private async Task SeedPaidJobAsync(int shopId, DateOnly dateIn, decimal price)
    {
        using var db = factory.CreateContextFor(shopId);
        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = $"Payer {Guid.NewGuid():N}"[..14], Phone = "+201000000000" },
            Plate = $"Q{Random.Shared.Next(1000, 9999)}", PlateKey = $"Q{Random.Shared.Next(1000, 9999)}", Make = "Kia", Model = "Rio", Year = 2020,
        };
        var job = new JobCard { Vehicle = vehicle, DateIn = dateIn, Status = JobStatus.Paid };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Paid work", Quantity = 1, UnitPrice = price });
        db.JobCards.Add(job);
        await db.SaveChangesAsync();
    }

    private async Task SeedOverdueReminderAsync(int shopId)
    {
        using var db = factory.CreateContextFor(shopId);
        var vehicle = new Vehicle
        {
            Customer = new Customer { Name = $"Late {Guid.NewGuid():N}"[..14], Phone = "+201000000000" },
            Plate = $"L{Random.Shared.Next(1000, 9999)}", PlateKey = $"L{Random.Shared.Next(1000, 9999)}", Make = "Kia", Model = "Rio", Year = 2020,
        };
        db.Reminders.Add(new Reminder { Vehicle = vehicle, Type = "Oil change", DueDate = Today.AddDays(-5) });
        await db.SaveChangesAsync();
    }
}
