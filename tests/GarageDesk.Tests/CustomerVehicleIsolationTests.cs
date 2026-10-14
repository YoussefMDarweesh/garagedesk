using GarageDesk.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// Rule 2 in CLAUDE.md: every new shop-owned entity gets a tenant-isolation test.
// These are Customer's and Vehicle's, at the DbContext level.
public sealed class CustomerVehicleIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;
    private readonly int _customerA;
    private readonly int _vehicleA;

    public CustomerVehicleIsolationTests()
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

        using (var asA = NewContext(_shopA))
        {
            var customer = new Customer { Name = "Customer of A", Phone = "+201000000001" };
            customer.Vehicles.Add(new Vehicle { Plate = "A 111", PlateKey = "A111", Make = "Skoda", Model = "Octavia", Year = 2020 });
            asA.Customers.Add(customer);
            asA.SaveChanges();
            _customerA = customer.Id;
            _vehicleA = customer.Vehicles.Single().Id;
        }
        using (var asB = NewContext(_shopB))
        {
            var customer = new Customer { Name = "Customer of B", Phone = "+201000000002" };
            customer.Vehicles.Add(new Vehicle { Plate = "B 222", PlateKey = "B222", Make = "Kia", Model = "Rio", Year = 2019 });
            asB.Customers.Add(customer);
            asB.SaveChanges();
        }
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_sees_its_own_customers_and_vehicles()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal(["Customer of A"], asA.Customers.Select(c => c.Name).ToList());
        Assert.Equal(["A 111"], asA.Vehicles.Select(v => v.Plate).ToList());
        Assert.Equal(["Customer of B"], asB.Customers.Select(c => c.Name).ToList());
        Assert.Equal(["B 222"], asB.Vehicles.Select(v => v.Plate).ToList());
    }

    [Fact]
    public void A_shop_cannot_find_another_shops_rows_by_id()
    {
        using var asB = NewContext(_shopB);

        Assert.Null(asB.Customers.FirstOrDefault(c => c.Id == _customerA));
        Assert.Null(asB.Vehicles.FirstOrDefault(v => v.Id == _vehicleA));
        Assert.Null(asB.Customers.Find(_customerA));
    }

    [Fact]
    public void Searching_by_plate_never_reaches_another_shops_customers()
    {
        using var asB = NewContext(_shopB);

        Assert.Empty(asB.Customers.Where(c => c.Vehicles.Any(v => v.PlateKey.Contains("A111"))).ToList());
    }

    [Fact]
    public void Nobody_signed_in_or_without_a_shop_sees_nothing()
    {
        using var noShop = NewContext(null);

        Assert.Empty(noShop.Customers.ToList());
        Assert.Empty(noShop.Vehicles.ToList());
    }

    [Fact]
    public void New_rows_get_the_current_shops_id()
    {
        using var asA = NewContext(_shopA);
        var customer = new Customer { Name = "New", Phone = "+201000000009" };
        asA.Customers.Add(customer);
        asA.SaveChanges();

        Assert.Equal(_shopA, customer.ShopId);
    }

    [Fact]
    public void A_shop_cannot_write_customers_or_vehicles_for_another_shop()
    {
        using (var asA = NewContext(_shopA))
        {
            asA.Customers.Add(new Customer { ShopId = _shopB, Name = "Sneaky", Phone = "+201000000003" });
            Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
        }

        using (var asA = NewContext(_shopA))
        {
            asA.Vehicles.Add(new Vehicle { ShopId = _shopB, CustomerId = _customerA, Plate = "X 1", PlateKey = "X1", Make = "m", Model = "m", Year = 2000 });
            Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
        }
    }

    public void Dispose() => _connection.Dispose();
}
