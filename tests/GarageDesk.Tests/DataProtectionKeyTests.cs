using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageDesk.Tests;

/// <summary>
/// The key ring (login cookies, form tokens, share-link tokens) lives in the database, so it survives a restart or a
/// redeploy. Without this, every restart on the host would log everyone out and break existing share links.
/// </summary>
public class DataProtectionKeyTests
{
    /// <summary>The test app on a named in-memory database that outlives one factory, like a real database outlives a restart.</summary>
    private sealed class SharedDatabaseFactory(string connectionString) : GarageDeskWebFactory(connectionString);

    [Fact]
    public void Keys_are_stored_in_the_database_and_still_work_after_a_restart()
    {
        // A unique name per run, so this test never shares its database with another one.
        var connectionString = $"DataSource=keys-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        string protectedText;
        using (var before = new SharedDatabaseFactory(connectionString))
        {
            protectedText = before.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test").Protect("hello");

            using var scope = before.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GarageDesk.Web.Data.ApplicationDbContext>();
            Assert.NotEmpty(db.DataProtectionKeys.AsNoTracking().ToList());
        }

        // "Restart": a brand-new app on the same database reads the same keys and can still unprotect the text.
        using var after = new SharedDatabaseFactory(connectionString);
        var unprotected = after.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test").Unprotect(protectedText);

        Assert.Equal("hello", unprotected);
    }
}
