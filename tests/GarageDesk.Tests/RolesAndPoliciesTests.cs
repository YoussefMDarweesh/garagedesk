using System.Net;
using GarageDesk.Web.Data.Migrations;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>v1.3: roles, policies, and the guarantee that no page is left without one.</summary>
public class RolesAndPoliciesTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // The only pages of the app itself that anyone may open without signing in. Everything else must name a policy.
    // Adding a page here is a deliberate, reviewable decision.
    private static readonly HashSet<string> AnonymousPages = new(StringComparer.Ordinal)
    {
        "/Index",      // the landing page for visitors; the dashboard guards itself (see IndexModel)
        "/Privacy",
        "/Error",
        "/SetCulture", // the language switcher works before sign-in
    };

    // Pages of the Identity UI package that carry no attribute at all. This one only prints a fixed message
    // (the app's own 403 handling never redirects to it), so it exposes nothing.
    private static readonly HashSet<string> IdentityPagesWithoutAttributes = new(StringComparer.Ordinal)
    {
        "/Account/AccessDenied",
    };

    private static readonly HashSet<string> KnownPolicies = [Policies.SignedIn, Policies.Owner, Policies.Staff, Policies.Team];

    private List<PageActionDescriptor> AllPages() =>
        factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<PageActionDescriptor>().ToList();

    [Fact]
    public void Every_razor_page_declares_a_policy_or_is_on_the_anonymous_allow_list()
    {
        var pages = AllPages();
        Assert.True(pages.Count > 20, $"Expected to find the app's pages, found {pages.Count}.");

        var problems = new List<string>();
        foreach (var page in pages)
        {
            var path = page.ViewEnginePath;
            var anonymous = page.EndpointMetadata.OfType<IAllowAnonymous>().Any();
            var authorize = page.EndpointMetadata.OfType<IAuthorizeData>().ToList();

            if (page.AreaName == "Identity")
            {
                // Login, register, manage-account...: the framework's own pages, each either open or [Authorize].
                if (!anonymous && authorize.Count == 0 && !IdentityPagesWithoutAttributes.Contains(path))
                {
                    problems.Add($"{page.AreaName}:{path} has neither [Authorize] nor [AllowAnonymous]");
                }
                continue;
            }

            if (AnonymousPages.Contains(path))
            {
                if (!anonymous)
                {
                    problems.Add($"{path} is on the allow-list but is not marked [AllowAnonymous]");
                }
                continue;
            }

            if (anonymous)
            {
                problems.Add($"{path} is [AllowAnonymous] but is not on the explicit allow-list in this test");
            }

            var policies = authorize.Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (policies.Count == 0)
            {
                problems.Add($"{path} has no named authorization policy");
            }
            else if (policies.Any(p => !KnownPolicies.Contains(p!)))
            {
                problems.Add($"{path} uses an unknown policy: {string.Join(", ", policies)}");
            }
        }

        Assert.True(problems.Count == 0, "Pages without a proper policy:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void The_anonymous_allow_list_has_no_stale_entries()
    {
        var existing = AllPages().Where(p => p.AreaName is null).Select(p => p.ViewEnginePath).ToHashSet();
        foreach (var page in AnonymousPages)
        {
            Assert.Contains(page, existing);
        }
    }

    [Fact]
    public async Task Existing_users_become_active_owners_when_the_migration_runs()
    {
        // Existing rows get the column default. It must be Owner and active, or everybody would be locked out.
        var up = new AddRolesToUsers().UpOperations.OfType<AddColumnOperation>().ToList();
        Assert.Equal("Owner", up.Single(o => o.Name == "Role").DefaultValue);
        Assert.Equal(true, up.Single(o => o.Name == "IsActive").DefaultValue);
        Assert.Equal(0m, up.Single(o => o.Name == "CommissionPercent").DefaultValue);

        // And a brand-new registration (a user created with no role set) is an Owner too.
        var (userId, _) = await TestData.CreateOwnerAsync(factory, "First user");
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByIdAsync(userId);
        Assert.Equal(ShopRole.Owner, user!.Role);
        Assert.True(user.IsActive);
    }

    [Theory]
    // path, owner, cashier, technician
    [InlineData("/Customers", 200, 200, 403)]
    [InlineData("/Customers/Create", 200, 200, 403)]
    [InlineData("/Vehicles", 200, 200, 403)]
    [InlineData("/Vehicles/Create", 200, 200, 403)]
    [InlineData("/Jobs", 200, 200, 403)]
    [InlineData("/Jobs/Create", 200, 200, 403)]
    [InlineData("/Jobs/Details/{job}", 200, 200, 403)]
    [InlineData("/Jobs/Edit/{job}", 200, 200, 403)]
    [InlineData("/Jobs/Invoice/{job}", 200, 200, 403)]
    [InlineData("/Estimates", 200, 200, 403)]
    [InlineData("/Estimates/Create", 200, 200, 403)]
    [InlineData("/Reminders", 200, 200, 403)]
    [InlineData("/Receivables", 200, 200, 403)]
    [InlineData("/ShopSettings", 200, 403, 403)]
    [InlineData("/", 200, 200, 403)]
    public async Task Each_role_gets_the_right_answer_and_forbidden_is_a_403_not_a_404(string path, int owner, int cashier, int technician)
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Roles Garage");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var technicianId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);
        var job = await TestData.SeedJobAsync(factory, shop);
        path = path.Replace("{job}", job.ToString());

        Assert.Equal((HttpStatusCode)owner, (await factory.CreateClientFor(ownerId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)cashier, (await factory.CreateClientFor(cashierId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)technician, (await factory.CreateClientFor(technicianId, shop).GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_technician_cannot_post_payments_or_change_a_job_through_the_priced_pages()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "No Posting");
        var technicianId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done);
        var client = factory.CreateClientFor(technicianId, shop);

        // Authorization runs before anti-forgery validation, so a missing token still gives the real answer: 403.
        var pay = await client.PostAsync($"/Jobs/Details/{job}?handler=AddPayment",
            new FormUrlEncodedContent([new("NewPayment.Amount", "10"), new("NewPayment.Method", "Cash")]));
        Assert.Equal(HttpStatusCode.Forbidden, pay.StatusCode);

        var edit = await client.PostAsync($"/Jobs/Edit/{job}", new FormUrlEncodedContent([new("Input.Notes", "x")]));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        using var db = factory.CreateContextFor(shop);
        Assert.Empty(db.Payments.ToList());
    }

    [Fact]
    public async Task A_deactivated_user_is_refused_everywhere_even_with_a_valid_login()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Deactivated");
        var gone = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier, active: false);
        var client = factory.CreateClientFor(gone, shop);

        foreach (var path in new[] { "/", "/Customers", "/Jobs", "/Identity/Account/Manage" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        // The owner of the same shop is unaffected.
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientFor(ownerId, shop).GetAsync("/Customers")).StatusCode);
    }

    [Fact]
    public async Task A_shop_claim_that_disagrees_with_the_database_is_refused()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Claim A");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "Claim B");
        var userOfA = await TestData.CreateStaffAsync(factory, shopA, ShopRole.Owner);

        var forged = factory.CreateClientFor(userOfA, shopB);

        Assert.Equal(HttpStatusCode.Forbidden, (await forged.GetAsync("/Customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await forged.GetAsync("/ShopSettings")).StatusCode);
    }

    [Fact]
    public async Task A_cashier_never_sees_the_revenue_report_on_the_dashboard_but_the_owner_does()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Dashboard Roles");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, dateIn: DateOnly.FromDateTime(DateTime.Today));

        var owner = await TestData.BodyAsync(await factory.CreateClientFor(ownerId, shop).GetAsync("/"));
        var cashier = await TestData.BodyAsync(await factory.CreateClientFor(cashierId, shop).GetAsync("/"));

        Assert.Contains("Revenue this month", owner);
        Assert.DoesNotContain("Revenue this month", cashier);
        Assert.DoesNotContain("data-chart", cashier);
        Assert.Contains("Open job cards", cashier);
    }

    [Fact]
    public async Task The_menu_matches_the_role()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Menu Roles");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);

        var owner = await TestData.BodyAsync(await factory.CreateClientFor(ownerId, shop).GetAsync("/Customers"));
        var cashier = await TestData.BodyAsync(await factory.CreateClientFor(cashierId, shop).GetAsync("/Customers"));

        Assert.Contains("href=\"/ShopSettings\"", owner);
        Assert.DoesNotContain("href=\"/ShopSettings\"", cashier);
    }
}
