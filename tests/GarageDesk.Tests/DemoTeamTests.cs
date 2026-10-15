using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>v1.3: the demo seeder also creates a cashier and a technician (passwords from settings or random, printed once).</summary>
public class DemoTeamTests
{
    private static async Task<(GarageDeskWebFactory Factory, string Output, int ExitCode)> RunAsync(
        GarageDeskWebFactory? factory = null, Dictionary<string, string?>? extra = null)
    {
        factory ??= new GarageDeskWebFactory();
        var settings = new Dictionary<string, string?> { ["Demo:Email"] = "demo@example.com", ["Demo:Password"] = "Demo-Pass-1" };
        foreach (var pair in extra ?? [])
        {
            settings[pair.Key] = pair.Value;
        }

        var output = new StringWriter();
        var exit = await DemoDataCommand.RunAsync(factory.Services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), output);
        return (factory, output.ToString(), exit);
    }

    [Fact]
    public async Task The_seeder_adds_a_cashier_and_a_technician_with_configured_passwords()
    {
        var (factory, output, exit) = await RunAsync(extra: new()
        {
            ["Demo:CashierPassword"] = "Cashier-Pass-1",
            ["Demo:TechnicianPassword"] = "Tech-Pass-1!",
        });
        using var _f = factory;
        Assert.Equal(0, exit);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = (await users.FindByEmailAsync("demo@example.com"))!;
        var cashier = (await users.FindByEmailAsync("cashier@garagedesk.app"))!;
        var technician = (await users.FindByEmailAsync("technician@garagedesk.app"))!;

        Assert.Equal(ShopRole.Owner, owner.Role);
        Assert.Equal(ShopRole.Cashier, cashier.Role);
        Assert.Equal(ShopRole.Technician, technician.Role);
        Assert.Equal(owner.ShopId, cashier.ShopId);
        Assert.Equal(owner.ShopId, technician.ShopId);
        Assert.True(await users.CheckPasswordAsync(cashier, "Cashier-Pass-1"));
        Assert.True(await users.CheckPasswordAsync(technician, "Tech-Pass-1!"));
        Assert.Equal(10m, technician.CommissionPercent);

        // The configured passwords are never echoed.
        Assert.DoesNotContain("Cashier-Pass-1", output);
        Assert.DoesNotContain("Tech-Pass-1!", output);

        // The technician has jobs of the demo shop, so "My jobs" is not empty.
        using var db = factory.CreateContextFor(owner.ShopId!.Value);
        var assigned = await db.JobCards.CountAsync(j => j.AssignedToUserId == technician.Id);
        Assert.True(assigned > 5);
        Assert.True(await db.JobCards.AnyAsync(j => j.AssignedToUserId == null));
    }

    [Fact]
    public async Task Without_settings_random_passwords_are_printed_once_and_a_second_run_changes_nothing()
    {
        var (factory, output, exit) = await RunAsync();
        using var _f = factory;
        Assert.Equal(0, exit);
        Assert.Contains("cashier@garagedesk.app", output);
        Assert.Contains("technician@garagedesk.app", output);
        Assert.Equal(2, output.Split("(shown once, save it now)").Length - 1); // cashier and technician (the owner's was set)

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var technician = (await users.FindByEmailAsync("technician@garagedesk.app"))!;
        var stamp = technician.PasswordHash;
        int before;
        using (var db = factory.CreateContextFor(technician.ShopId!.Value))
        {
            before = await db.JobCards.CountAsync(j => j.AssignedToUserId == technician.Id);
        }

        var (_, second, secondExit) = await RunAsync(factory);
        Assert.Equal(0, secondExit);
        Assert.Contains("already existed", second);
        Assert.DoesNotContain("shown once", second);

        technician = (await users.FindByEmailAsync("technician@garagedesk.app"))!;
        Assert.Equal(stamp, technician.PasswordHash);
        using var after = factory.CreateContextFor(technician.ShopId!.Value);
        Assert.Equal(before, await after.JobCards.CountAsync(j => j.AssignedToUserId == technician.Id));
    }
}
