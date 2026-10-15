using System.Security.Cryptography;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// "dotnet run --project src/GarageDesk.Web -- --seed-demo": creates the Demo Motors shop, its owner login and
/// its data, then exits without starting the web server. Safe to run twice: it only fills a shop that is empty.
///
/// Settings (user-secrets locally, environment variables anywhere else):
///   Demo:Email     the owner's login, default demo@garagedesk.app
///   Demo:Password  the owner's password; if you don't set one a random one is made and printed once
/// </summary>
public static class DemoDataCommand
{
    public const string Flag = "--seed-demo";

    private const string DefaultEmail = "demo@garagedesk.app";

    /// <returns>0 if the demo shop is ready (new or already there), 1 if it could not be created.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();

        // Never create tables ourselves: the database must already be on the latest migration.
        if (db.Database.IsNpgsql())
        {
            IEnumerable<string> pending;
            try
            {
                pending = await db.Database.GetPendingMigrationsAsync();
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync("Could not reach the database. Check the ConnectionStrings:Default setting.");
                await output.WriteLineAsync(ex.GetBaseException().Message);
                return 1;
            }

            if (pending.Any())
            {
                await output.WriteLineAsync("The database has migrations that are not applied yet. Run this first, then try again:");
                await output.WriteLineAsync("  dotnet ef database update --project src/GarageDesk.Web");
                return 1;
            }
        }

        var email = configuration["Demo:Email"] is { Length: > 0 } configuredEmail ? configuredEmail : DefaultEmail;
        var password = configuration["Demo:Password"];
        var generatedPassword = false;
        if (string.IsNullOrEmpty(password))
        {
            password = GeneratePassword();
            generatedPassword = true;
        }

        var owner = await users.FindByEmailAsync(email);
        var createdOwner = false;
        if (owner is null)
        {
            owner = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Shop = new Shop { Name = DemoData.ShopName, Phone = DemoData.ShopPhone, Address = DemoData.ShopAddress },
            };
            var created = await users.CreateAsync(owner, password);
            if (!created.Succeeded)
            {
                await output.WriteLineAsync("Could not create the demo owner: " + string.Join("; ", created.Errors.Select(e => e.Description)));
                return 1;
            }
            createdOwner = true;
        }
        else if (owner.ShopId is null)
        {
            await output.WriteLineAsync($"{email} exists but has no shop. Pick another address with Demo:Email.");
            return 1;
        }

        // Act as the demo shop, the same way a signed-in request would, so every row gets the right ShopId.
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var shopDb = new ApplicationDbContext(options, new FixedShop(owner.ShopId!.Value));
        var populated = await DemoData.PopulateAsync(shopDb, ShopClock.Today());

        await output.WriteLineAsync(populated
            ? $"Created the demo data for {DemoData.ShopName}."
            : $"{DemoData.ShopName} already has data, nothing was changed.");

        if (createdOwner)
        {
            await output.WriteLineAsync($"Login:    {email}");
            await output.WriteLineAsync(generatedPassword
                ? $"Password: {password}   (shown once, save it now)"
                : "Password: the value of Demo:Password");
        }
        else
        {
            await output.WriteLineAsync($"Login:    {email} (already existed, password unchanged)");
        }

        // The team: a cashier and a technician in the same shop, so every role can be tried.
        var shopId = owner.ShopId!.Value;
        await EnsureStaffAsync(users, configuration, output, shopId, ShopRole.Cashier, "Demo:CashierEmail", "Demo:CashierPassword",
            "cashier@garagedesk.app", "Demo Cashier");
        var technician = await EnsureStaffAsync(users, configuration, output, shopId, ShopRole.Technician, "Demo:TechnicianEmail", "Demo:TechnicianPassword",
            "technician@garagedesk.app", "Demo Technician");

        // Give the technician some of the shop's jobs and a commission, once (never over jobs someone already assigned).
        if (technician is { Created: true } && !await shopDb.JobCards.AnyAsync(j => j.AssignedToUserId != null))
        {
            var jobs = await shopDb.JobCards.OrderBy(j => j.Id).ToListAsync();
            foreach (var job in jobs.Where((_, index) => index % 3 == 0))
            {
                job.AssignedToUserId = technician.User.Id;
            }

            technician.User.CommissionPercent = 10;
            await users.UpdateAsync(technician.User);
            await shopDb.SaveChangesAsync();
            await output.WriteLineAsync($"Gave {jobs.Count(j => j.AssignedToUserId != null)} job cards to the demo technician (10% commission).");
        }

        return 0;
    }

    private sealed record StaffResult(AppUser User, bool Created);

    /// <summary>
    /// Creates a staff login in the demo shop if it does not exist yet. The password comes from the setting, or is
    /// random and printed once. Nothing is stored in the repository.
    /// </summary>
    private static async Task<StaffResult?> EnsureStaffAsync(
        Microsoft.AspNetCore.Identity.UserManager<AppUser> users, IConfiguration configuration, TextWriter output,
        int shopId, ShopRole role, string emailSetting, string passwordSetting, string defaultEmail, string fullName)
    {
        var email = configuration[emailSetting] is { Length: > 0 } configured ? configured : defaultEmail;
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (existing.ShopId != shopId)
            {
                await output.WriteLineAsync($"{email} already belongs to another shop. Pick another address with {emailSetting}.");
                return null;
            }

            await output.WriteLineAsync($"{role,-10} {email} (already existed, password unchanged)");
            return new StaffResult(existing, false);
        }

        var password = configuration[passwordSetting];
        var generated = string.IsNullOrEmpty(password);
        if (generated)
        {
            password = GeneratePassword();
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, ShopId = shopId, Role = role, IsActive = true, FullName = fullName };
        var created = await users.CreateAsync(user, password!);
        if (!created.Succeeded)
        {
            await output.WriteLineAsync($"Could not create the demo {role.ToString().ToLowerInvariant()}: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            return null;
        }

        await output.WriteLineAsync($"{role,-10} {email}");
        await output.WriteLineAsync(generated
            ? $"           Password: {password}   (shown once, save it now)"
            : $"           Password: the value of {passwordSetting}");
        return new StaffResult(user, true);
    }

    /// <summary>16 characters that satisfy Identity's default rules (upper, lower, digit, symbol) and skip look-alikes.</summary>
    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        while (true)
        {
            var chars = new char[14];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var text = new string(chars);
            if (text.Any(char.IsUpper) && text.Any(char.IsLower) && text.Any(char.IsDigit))
            {
                return text[..7] + "-" + text[7..] + "!";
            }
        }
    }

    /// <summary>A "current shop" that is simply a given id, for work done outside a web request.</summary>
    private sealed class FixedShop(int shopId) : ICurrentShop
    {
        public int? ShopId => shopId;
    }
}
