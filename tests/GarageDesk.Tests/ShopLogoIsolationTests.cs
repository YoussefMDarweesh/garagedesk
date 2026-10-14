using GarageDesk.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// Rule 2 in CLAUDE.md: every new shop-owned entity gets a tenant-isolation test. This is ShopLogo's.
public sealed class ShopLogoIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;

    public ShopLogoIsolationTests()
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
            asA.ShopLogos.Add(new ShopLogo { Bytes = [1, 1, 1], ContentType = "image/png", UpdatedAtUtc = DateTime.UtcNow });
            asA.SaveChanges();
        }
        using (var asB = NewContext(_shopB))
        {
            asB.ShopLogos.Add(new ShopLogo { Bytes = [2, 2, 2], ContentType = "image/jpeg", UpdatedAtUtc = DateTime.UtcNow });
            asB.SaveChanges();
        }
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_sees_its_own_logo()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal([1, 1, 1], asA.ShopLogos.Single().Bytes);
        Assert.Equal([2, 2, 2], asB.ShopLogos.Single().Bytes);
    }

    [Fact]
    public void Nobody_signed_in_or_without_a_shop_sees_no_logo()
    {
        using var noShop = NewContext(null);

        Assert.Empty(noShop.ShopLogos.ToList());
    }

    [Fact]
    public void A_shop_cannot_write_a_logo_for_another_shop()
    {
        using var asA = NewContext(_shopA);
        asA.ShopLogos.Add(new ShopLogo { ShopId = _shopB, Bytes = [9], ContentType = "image/png", UpdatedAtUtc = DateTime.UtcNow });

        Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
    }

    [Fact]
    public void A_shop_cannot_have_two_logos()
    {
        using var asA = NewContext(_shopA);
        asA.ShopLogos.Add(new ShopLogo { Bytes = [9], ContentType = "image/png", UpdatedAtUtc = DateTime.UtcNow });

        Assert.Throws<DbUpdateException>(() => asA.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();
}
