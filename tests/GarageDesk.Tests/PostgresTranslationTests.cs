using GarageDesk.Web.Data;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>
/// Every other test runs on SQLite, but production is PostgreSQL. These make sure the app's search queries
/// can be translated to PostgreSQL SQL too (nothing is executed, so no database is needed).
/// </summary>
public class PostgresTranslationTests
{
    private static ApplicationDbContext NewPostgresContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .Options,
            new FakeCurrentShop(1));

    [Theory]
    [InlineData("Ahmed")]
    [InlineData("0101 234 5678")]
    [InlineData("ABC-1234")]
    [InlineData("أ ب ج 1234")]
    [InlineData("")]
    public void The_customer_search_translates_to_postgresql(string query)
    {
        using var db = NewPostgresContext();

        var sql = db.Customers.AsNoTracking().Search(query)
            .Include(c => c.Vehicles.OrderBy(v => v.Plate))
            .OrderBy(c => c.Name)
            .Take(101)
            .ToQueryString();

        Assert.Contains("\"Customers\"", sql);
        Assert.Contains("\"ShopId\"", sql); // the tenant filter is part of the query
    }

    [Theory]
    [InlineData("Skoda")]
    [InlineData("XYZ 999")]
    [InlineData("")]
    public void The_vehicle_search_translates_to_postgresql(string query)
    {
        using var db = NewPostgresContext();

        var sql = db.Vehicles.AsNoTracking().Search(query)
            .Include(v => v.Customer)
            .OrderBy(v => v.Plate)
            .Take(101)
            .ToQueryString();

        Assert.Contains("\"Vehicles\"", sql);
        Assert.Contains("\"ShopId\"", sql);
    }
}
