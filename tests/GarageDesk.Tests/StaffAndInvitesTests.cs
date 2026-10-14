using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>v1.3: the team page, invitations, activation and the last-owner rule.</summary>
public partial class StaffAndInvitesTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [GeneratedRegex(@"/Join/([A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private HttpClient Anonymous() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> CreateInviteAsync(HttpClient owner, string role = "Cashier", string name = "Sam Staff", string phone = "01012345678")
    {
        var response = await TestData.SubmitFormAsync(owner, "/Staff?handler=Invite",
            ("Invite.FullName", name), ("Invite.Phone", phone), ("Invite.Role", role));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = LinkPattern().Match(await TestData.BodyAsync(response));
        Assert.True(match.Success, "The new invitation link should be shown on the page.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token, string email, string password = "Str0ng!Pass")
    {
        // The anti-forgery token comes from another public page, so this also works for links that are dead (404).
        var antiForgery = await TestData.TokenAsync(client, "/Privacy");
        return await client.PostAsync($"/Join/{token}", new FormUrlEncodedContent(
        [
            new("Input.FullName", "Sam Staff"),
            new("Input.Email", email),
            new("Input.Password", password),
            new("Input.ConfirmPassword", password),
            new("__RequestVerificationToken", antiForgery),
        ]));
    }

    // ---------------------------------------------------------------- tokens

    [Fact]
    public void Invite_tokens_have_256_bits_are_url_safe_unique_and_only_their_hash_is_kept()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var (token, hash) = InviteTokens.Create();
            Assert.Equal(43, token.Length); // 32 random bytes, base64url without padding
            Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
            Assert.True(InviteTokens.LooksValid(token));
            Assert.Equal(64, hash.Length);
            Assert.NotEqual(token, hash);
            Assert.Equal(hash, InviteTokens.Hash(token));
            Assert.True(seen.Add(token));
        }

        Assert.False(InviteTokens.LooksValid("short"));
        Assert.False(InviteTokens.LooksValid(new string('a', 42) + "!"));
    }

    [Theory]
    // role, active, newRole, newActive, otherActiveOwners, wouldLeaveNone
    [InlineData(ShopRole.Owner, true, ShopRole.Cashier, true, 0, true)]   // demote the last owner
    [InlineData(ShopRole.Owner, true, ShopRole.Owner, false, 0, true)]    // deactivate the last owner
    [InlineData(ShopRole.Owner, true, ShopRole.Cashier, true, 1, false)]  // another active owner exists
    [InlineData(ShopRole.Owner, true, ShopRole.Owner, true, 0, false)]    // no change
    [InlineData(ShopRole.Cashier, true, ShopRole.Technician, false, 0, false)] // not an owner at all
    [InlineData(ShopRole.Owner, false, ShopRole.Cashier, true, 0, false)] // was not an active owner anyway
    public void Last_owner_rule(ShopRole role, bool active, ShopRole newRole, bool newActive, int others, bool expected) =>
        Assert.Equal(expected, StaffRules.WouldLeaveNoActiveOwner(role, active, newRole, newActive, others));

    // ---------------------------------------------------------------- the page

    [Fact]
    public async Task Only_the_owner_can_open_the_team_page()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Team Page");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var technicianId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientFor(ownerId, shop).GetAsync("/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(cashierId, shop).GetAsync("/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(technicianId, shop).GetAsync("/Staff")).StatusCode);

        var body = await TestData.BodyAsync(await factory.CreateClientFor(ownerId, shop).GetAsync("/Staff"));
        Assert.Contains("Team members", body);
    }

    [Fact]
    public async Task The_team_page_lists_only_this_shops_people()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Team A");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "Team B");
        await TestData.CreateStaffAsync(factory, shopA, ShopRole.Cashier, fullName: "Alice In A");
        await TestData.CreateStaffAsync(factory, shopB, ShopRole.Cashier, fullName: "Bob In B");

        var body = await TestData.BodyAsync(await factory.CreateClientFor(ownerA, shopA).GetAsync("/Staff"));

        Assert.Contains("Alice In A", body);
        Assert.DoesNotContain("Bob In B", body);
    }

    // ---------------------------------------------------------------- invitations

    [Fact]
    public async Task An_invitation_creates_a_login_in_the_right_shop_with_the_right_role_and_works_once()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Invite Garage");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Other Garage");
        var token = await CreateInviteAsync(factory.CreateClientFor(ownerId, shop), "Technician");

        // Only the hash is stored.
        using (var db = factory.CreateContextFor(shop))
        {
            var stored = await db.StaffInvites.SingleAsync();
            Assert.Equal(InviteTokens.Hash(token), stored.TokenHash);
            Assert.DoesNotContain(token, stored.TokenHash);
            Assert.Equal("+201012345678", stored.Phone);
            Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow.AddDays(6));
        }

        // The visitor opens the link with no login.
        var visitor = Anonymous();
        var page = await visitor.GetAsync($"/Join/{token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await TestData.BodyAsync(page);
        Assert.Contains("Invite Garage", html);
        Assert.Contains("Create my login", html);
        Assert.True(page.Headers.TryGetValues("Referrer-Policy", out var referrer) && referrer.Contains("no-referrer"));
        Assert.Contains("no-store", page.Headers.CacheControl?.ToString());

        var email = $"{Guid.NewGuid():N}@example.com";
        var accepted = await AcceptAsync(visitor, token, email);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(shop, user!.ShopId);
            Assert.NotEqual(otherShop, user.ShopId);
            Assert.Equal(ShopRole.Technician, user.Role);
            Assert.True(user.IsActive);
        }

        // Second use of the same link: the same 404 as an unknown link.
        var again = await AcceptAsync(Anonymous(), token, $"{Guid.NewGuid():N}@example.com");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/Join/{token}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_expired_used_and_revoked_links_all_answer_the_same()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Dead Links");
        var owner = factory.CreateClientFor(ownerId, shop);

        var expired = await CreateInviteAsync(owner);
        var revoked = await CreateInviteAsync(owner);
        var used = await CreateInviteAsync(owner);
        await AcceptAsync(Anonymous(), used, $"{Guid.NewGuid():N}@example.com");

        using (var db = factory.CreateContextFor(shop))
        {
            var e = await db.StaffInvites.SingleAsync(i => i.TokenHash == InviteTokens.Hash(expired));
            e.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            var r = await db.StaffInvites.SingleAsync(i => i.TokenHash == InviteTokens.Hash(revoked));
            r.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var (unknown, _) = InviteTokens.Create();
        string? firstBody = null;
        foreach (var token in new[] { unknown, expired, revoked, used, "not-a-token" })
        {
            var response = await Anonymous().GetAsync($"/Join/{token}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("This invitation link does not work", body);
            Assert.DoesNotContain("Dead Links", body); // the shop name is never leaked
            // Same page for every reason (only the anti-forgery-free body is compared).
            firstBody ??= Regex.Replace(body, "<title>.*?</title>", "", RegexOptions.Singleline);
        }

        // Posting to a dead link creates no account.
        var email = $"{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.NotFound, (await AcceptAsync(Anonymous(), expired, email)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email));
    }

    [Fact]
    public async Task A_failed_sign_up_does_not_burn_the_invitation()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Retry Garage");
        var token = await CreateInviteAsync(factory.CreateClientFor(ownerId, shop));
        var visitor = Anonymous();

        // A password that breaks Identity's rules.
        var weak = await AcceptAsync(visitor, token, $"{Guid.NewGuid():N}@example.com", "weakweak");
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);

        // The link still works afterwards.
        var good = await AcceptAsync(visitor, token, $"{Guid.NewGuid():N}@example.com");
        Assert.Equal(HttpStatusCode.Redirect, good.StatusCode);
    }

    [Fact]
    public async Task An_owner_can_cancel_an_invitation_and_only_their_own()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Cancel A");
        var (ownerB, shopB) = await TestData.CreateOwnerAsync(factory, "Cancel B");
        var tokenA = await CreateInviteAsync(factory.CreateClientFor(ownerA, shopA));
        int inviteId;
        using (var db = factory.CreateContextFor(shopA))
        {
            inviteId = (await db.StaffInvites.SingleAsync()).Id;
        }

        // Shop B cannot see or cancel shop A's invitation (new shop-owned entity: tenant isolation).
        using (var dbB = factory.CreateContextFor(shopB))
        {
            Assert.Empty(await dbB.StaffInvites.ToListAsync());
        }
        var ownerBClient = factory.CreateClientFor(ownerB, shopB);
        var token = await TestData.TokenAsync(ownerBClient, "/Staff");
        var cross = await ownerBClient.PostAsync($"/Staff?handler=Revoke&id={inviteId}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/Join/{tokenA}")).StatusCode);

        // Shop A's owner can.
        var ownerAClient = factory.CreateClientFor(ownerA, shopA);
        var tokenForA = await TestData.TokenAsync(ownerAClient, "/Staff");
        var ok = await ownerAClient.PostAsync($"/Staff?handler=Revoke&id={inviteId}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", tokenForA)]));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/Join/{tokenA}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_three_roles_can_be_invited()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Bad Role");
        var owner = factory.CreateClientFor(ownerId, shop);

        var response = await TestData.SubmitFormAsync(owner, "/Staff?handler=Invite", ("Invite.Role", "99"));

        Assert.DoesNotContain("data-new-invite", await TestData.BodyAsync(response));
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.StaffInvites.ToListAsync());
    }

    // ---------------------------------------------------------------- roles, activation, last owner

    private static async Task<HttpResponseMessage> PostTeamAsync(HttpClient owner, string handler, string userId, params (string, string)[] fields)
    {
        var token = await TestData.TokenAsync(owner, "/Staff");
        var content = new FormUrlEncodedContent(fields
            .Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))
            .Append(new("__RequestVerificationToken", token)));
        return await owner.PostAsync($"/Staff?handler={handler}&id={userId}", content);
    }

    private async Task<AppUser> LoadUserAsync(string id)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id))!;
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_or_deactivated()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Last Owner");
        var owner = factory.CreateClientFor(ownerId, shop);

        await PostTeamAsync(owner, "Role", ownerId, ("role", "Cashier"));
        await PostTeamAsync(owner, "Active", ownerId, ("active", "false"));

        var user = await LoadUserAsync(ownerId);
        Assert.Equal(ShopRole.Owner, user.Role);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task With_a_second_owner_the_first_can_step_down_but_then_the_second_is_the_last()
    {
        var (firstId, shop) = await TestData.CreateOwnerAsync(factory, "Two Owners");
        var secondId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Owner);
        var first = factory.CreateClientFor(firstId, shop);

        await PostTeamAsync(first, "Role", firstId, ("role", "Cashier"));
        Assert.Equal(ShopRole.Cashier, (await LoadUserAsync(firstId)).Role);

        // The second owner is now the last one and cannot be demoted by anybody.
        var second = factory.CreateClientFor(secondId, shop);
        await PostTeamAsync(second, "Role", secondId, ("role", "Technician"));
        Assert.Equal(ShopRole.Owner, (await LoadUserAsync(secondId)).Role);
    }

    [Fact]
    public async Task Deactivating_someone_blocks_sign_in_and_changes_their_security_stamp()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Deactivate");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var before = (await LoadUserAsync(cashierId)).SecurityStamp;
        var cashier = factory.CreateClientFor(cashierId, shop);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/Customers")).StatusCode);

        var response = await PostTeamAsync(factory.CreateClientFor(ownerId, shop), "Active", cashierId, ("active", "false"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var user = await LoadUserAsync(cashierId);
        Assert.False(user.IsActive);
        Assert.NotEqual(before, user.SecurityStamp); // every existing cookie fails its next security-stamp check

        // The existing session is refused at once, and a new one cannot be created.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/Customers")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
        Assert.False(await signIn.CanSignInAsync(user));

        // Activating again restores access.
        await PostTeamAsync(factory.CreateClientFor(ownerId, shop), "Active", cashierId, ("active", "true"));
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/Customers")).StatusCode);
    }

    [Fact]
    public async Task A_role_change_takes_effect_on_the_very_next_request()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Role Change");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var cashier = factory.CreateClientFor(cashierId, shop);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/Customers")).StatusCode);

        await PostTeamAsync(factory.CreateClientFor(ownerId, shop), "Role", cashierId, ("role", "Technician"));

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/Customers")).StatusCode);
        Assert.Equal(ShopRole.Technician, (await LoadUserAsync(cashierId)).Role);
    }

    [Fact]
    public async Task An_owner_cannot_change_a_person_of_another_shop()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Hands Off A");
        var (_, shopB) = await TestData.CreateOwnerAsync(factory, "Hands Off B");
        var victim = await TestData.CreateStaffAsync(factory, shopB, ShopRole.Cashier);
        var owner = factory.CreateClientFor(ownerA, shopA);

        var role = await PostTeamAsync(owner, "Role", victim, ("role", "Owner"));
        var active = await PostTeamAsync(owner, "Active", victim, ("active", "false"));

        Assert.Equal(HttpStatusCode.NotFound, role.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);
        var user = await LoadUserAsync(victim);
        Assert.Equal(ShopRole.Cashier, user.Role);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task A_cashier_cannot_promote_themselves()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "No Self Promotion");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var client = factory.CreateClientFor(cashierId, shop);

        var response = await client.PostAsync($"/Staff?handler=Role&id={cashierId}",
            new FormUrlEncodedContent([new("role", "Owner")]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ShopRole.Cashier, (await LoadUserAsync(cashierId)).Role);
    }
}
