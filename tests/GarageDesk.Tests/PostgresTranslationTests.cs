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
    public void The_vehicle_service_history_query_translates_to_postgresql()
    {
        using var db = NewPostgresContext();

        var sql = db.JobCards.AsNoTracking()
            .Where(j => j.VehicleId == 7)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .ToQueryString();

        Assert.Contains("\"JobCards\"", sql);
        Assert.Contains("\"VehicleId\"", sql);
        Assert.Contains("DESC", sql);
    }

    [Fact]
    public void The_dashboard_reminder_query_translates_to_postgresql()
    {
        using var db = NewPostgresContext();

        var sql = db.Reminders.AsNoTracking()
            .DueOrOverdue(new DateOnly(2026, 10, 14))
            .Include(r => r.Vehicle).ThenInclude(v => v!.Customer)
            .Take(200)
            .ToQueryString();

        Assert.Contains("\"Reminders\"", sql);
        Assert.Contains("\"IsDone\"", sql);
        Assert.Contains("\"DueMileage\"", sql);
        Assert.Contains("\"ShopId\"", sql);
    }

    [Fact]
    public void The_dashboard_activity_query_translates_to_postgresql()
    {
        using var db = NewPostgresContext();
        var first = new DateOnly(2026, 1, 1);

        var sql = db.JobCards.AsNoTracking()
            .Where(j => j.DateIn >= first)
            .Select(j => new
            {
                j.DateIn,
                j.Status,
                Lines = j.Lines.Select(l => new { l.Quantity, l.UnitPrice }).ToList(),
            })
            .ToQueryString();

        Assert.Contains("\"JobCards\"", sql);
        Assert.Contains("\"UnitPrice\"", sql);
        Assert.Contains("\"ShopId\"", sql);
    }

    [Fact]
    public void The_paged_lists_translate_to_postgresql()
    {
        using var db = NewPostgresContext();

        var customers = db.Customers.AsNoTracking().Search("Ahmed")
            .Include(c => c.Vehicles.OrderBy(v => v.Plate))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(20).Take(20)
            .ToQueryString();
        var vehicles = db.Vehicles.AsNoTracking().Search("ABC")
            .Include(v => v.Customer)
            .OrderBy(v => v.Plate).ThenBy(v => v.Id)
            .Skip(20).Take(20)
            .ToQueryString();
        var count = db.JobCards.AsNoTracking().Search("#12")
            .Where(j => j.Status == GarageDesk.Web.Models.JobStatus.Paid)
            .ToQueryString();

        Assert.Contains("OFFSET", customers);
        Assert.Contains("LIMIT", customers);
        Assert.Contains("OFFSET", vehicles);
        Assert.Contains("\"JobCards\"", count);
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
