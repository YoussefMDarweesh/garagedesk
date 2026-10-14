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
    [InlineData("ABC-1234")]
    [InlineData("Ahmed")]
    [InlineData("#12")]
    [InlineData("")]
    public void The_job_card_list_and_search_translate_to_postgresql(string query)
    {
        using var db = NewPostgresContext();

        var sql = db.JobCards.AsNoTracking().Search(query)
            .Where(j => j.Status == GarageDesk.Web.Models.JobStatus.Done)
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Take(101)
            .ToQueryString();

        Assert.Contains("\"JobCards\"", sql);
        Assert.Contains("\"ShopId\"", sql);
    }

    [Fact]
    public void The_open_job_count_translates_to_postgresql()
    {
        using var db = NewPostgresContext();

        var sql = db.JobCards.Where(j => j.Status != GarageDesk.Web.Models.JobStatus.Paid).ToQueryString();

        Assert.Contains("\"Status\"", sql);
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
