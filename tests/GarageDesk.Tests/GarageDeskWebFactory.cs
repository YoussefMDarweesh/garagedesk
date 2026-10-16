using GarageDesk.Web.Data;
using Microsoft.AspNetCore.Authentication;
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
    private readonly SqliteConnection _connection;

    public GarageDeskWebFactory() : this("DataSource=:memory:") { }

    /// <summary>Two factories on the same named, shared in-memory database behave like one server restarted.</summary>
    protected GarageDeskWebFactory(string sqliteConnectionString) => _connection = new(sqliteConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Program.cs insists on a connection string; this dummy is never used to connect.
        builder.UseSetting("ConnectionStrings:Default", "Host=unused;Database=unused");

        // Every test request comes from the same (unknown) address, so the invitation page's rate limit is set high
        // here; the test that checks the limit uses its own factory with a low one.
        builder.UseSetting("Invites:PermitPerMinute", "100000");

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

            // Tests sign in by sending headers (see TestAuthHandler) instead of using the login form.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>A DbContext on the same in-memory database, acting as the given shop. For seeding test data directly.</summary>
    public ApplicationDbContext CreateContextFor(int shopId) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    /// <summary>A client that is signed in as the given user (and, optionally, has that shop in its claims).</summary>
    public HttpClient CreateClientFor(string userId, int? shopId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (shopId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ShopHeader, shopId.Value.ToString());
        }
        return client;
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
