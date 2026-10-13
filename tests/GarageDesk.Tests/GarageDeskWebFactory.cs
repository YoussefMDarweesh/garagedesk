using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GarageDesk.Tests;

/// <summary>
/// Boots the real web app, but swaps PostgreSQL for a throw-away SQLite in-memory database,
/// so tests (and CI) never touch Neon.
/// </summary>
public class GarageDeskWebFactory : WebApplicationFactory<Program>
{
    // An in-memory SQLite database lives only as long as its connection, so we keep one open.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Program.cs insists on a connection string; this dummy is never used to connect.
        builder.UseSetting("ConnectionStrings:Default", "Host=unused;Database=unused");

        builder.ConfigureServices(services =>
        {
            // Remove everything AddDbContext<ApplicationDbContext>(UseNpgsql) registered
            // (DbContextOptions<T> and EF Core's options-configuration for T), then register SQLite.
            var descriptors = services
                .Where(d => d.ServiceType.IsGenericType
                            && d.ServiceType.GenericTypeArguments.Contains(typeof(ApplicationDbContext)))
                .ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
