using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// Rule 2 in CLAUDE.md: every new shop-owned entity gets a tenant-isolation test.
// This is Reminder's, at the DbContext level.
public sealed class ReminderIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;
    private readonly int _reminderA;
    private readonly int _vehicleA;

    public ReminderIsolationTests()
    {
        _connection.Open();

        using var setup = NewContext(null);
        setup.Database.EnsureCreated();
        var a = new Shop { Name = "Shop A", Phone = "01000000001", Address = "Cairo" };
        var b = new Shop { Name = "Shop B", Phone = "01000000002", Address = "Giza" };
        setup.Shops.AddRange(a, b);
        setup.SaveChanges();
        _shopA = a.Id;
        _shopB = b.Id;

        (_reminderA, _vehicleA) = Seed(_shopA, "A");
        Seed(_shopB, "B");
    }

    private (int ReminderId, int VehicleId) Seed(int shopId, string tag)
    {
        using var db = NewContext(shopId);
        var customer = new Customer { Name = $"Customer {tag}", Phone = "+201000000000" };
        var vehicle = new Vehicle { Customer = customer, Plate = $"{tag} 1", PlateKey = $"{tag}1", Make = "Kia", Model = "Rio", Year = 2020 };
        var reminder = new Reminder { Vehicle = vehicle, Type = $"Oil change {tag}", DueDate = new DateOnly(2026, 1, 1) };
        db.Reminders.Add(reminder);
        db.SaveChanges();
        return (reminder.Id, vehicle.Id);
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_sees_its_own_reminders()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal(["Oil change A"], asA.Reminders.Select(r => r.Type).ToList());
        Assert.Equal(["Oil change B"], asB.Reminders.Select(r => r.Type).ToList());
    }

    [Fact]
    public void The_dashboard_query_never_returns_another_shops_reminders()
    {
        using var asB = NewContext(_shopB);

        var due = asB.Reminders.DueOrOverdue(new DateOnly(2026, 10, 14)).ToList();

        Assert.Equal(["Oil change B"], due.Select(r => r.Type));
        Assert.DoesNotContain(due, r => r.Id == _reminderA);
    }

    [Fact]
    public void A_shop_cannot_find_another_shops_reminder_by_id()
    {
        using var asB = NewContext(_shopB);

        Assert.Null(asB.Reminders.FirstOrDefault(r => r.Id == _reminderA));
    }

    [Fact]
    public void Nobody_signed_in_or_without_a_shop_sees_nothing()
    {
        using var noShop = NewContext(null);

        Assert.Empty(noShop.Reminders.ToList());
    }

    [Fact]
    public void A_shop_cannot_write_a_reminder_for_another_shop()
    {
        using var asA = NewContext(_shopA);
        asA.Reminders.Add(new Reminder { ShopId = _shopB, VehicleId = _vehicleA, Type = "Sneaky", DueDate = new DateOnly(2026, 1, 1) });

        Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();
}
