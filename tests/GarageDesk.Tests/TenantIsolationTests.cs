using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// No shop-owned entity exists yet (Customer arrives in M2), so these tests use a stand-in entity.
// It only has to implement IShopOwned, exactly as Customer, Vehicle, ... will.
public class Note : IShopOwned
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public string Text { get; set; } = "";
}

public class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentShop currentShop)
    : ApplicationDbContext(options, currentShop)
{
    public DbSet<Note> Notes => Set<Note>();
}

public class FakeCurrentShop(int? shopId) : ICurrentShop
{
    public int? ShopId { get; } = shopId;
}

public sealed class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;

    public TenantIsolationTests()
    {
        _connection.Open();

        using var setup = NewContext(shopId: null);
        setup.Database.EnsureCreated();

        var a = new Shop { Name = "Shop A", Phone = "01000000001", Address = "Cairo" };
        var b = new Shop { Name = "Shop B", Phone = "01000000002", Address = "Giza" };
        setup.Shops.AddRange(a, b);
        setup.SaveChanges();
        _shopA = a.Id;
        _shopB = b.Id;

        // Each shop writes its own note, through its own context, like real requests would.
        using (var asA = NewContext(_shopA))
        {
            asA.Notes.Add(new Note { Text = "A's secret" });
            asA.SaveChanges();
        }
        using (var asB = NewContext(_shopB))
        {
            asB.Notes.Add(new Note { Text = "B's secret" });
            asB.SaveChanges();
        }
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_reads_its_own_rows()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal(["A's secret"], asA.Notes.Select(n => n.Text).ToList());
        Assert.Equal(["B's secret"], asB.Notes.Select(n => n.Text).ToList());
    }

    [Fact]
    public void Shop_B_cannot_find_shop_A_row_by_id()
    {
        int idOfAsNote;
        using (var asA = NewContext(_shopA))
        {
            idOfAsNote = asA.Notes.Single().Id;
        }

        using var asB = NewContext(_shopB);

        Assert.Null(asB.Notes.FirstOrDefault(n => n.Id == idOfAsNote));
        Assert.Null(asB.Notes.Find(idOfAsNote));
    }

    [Fact]
    public void The_filter_is_what_hides_the_rows()
    {
        // Sanity check: both rows really are in the table. Without the filter you'd see both.
        using var asA = NewContext(_shopA);

        Assert.Equal(2, asA.Notes.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void Nobody_signed_in_or_no_shop_yet_sees_nothing()
    {
        using var noShop = NewContext(shopId: null);

        Assert.Empty(noShop.Notes.ToList());
        Assert.Empty(noShop.Shops.ToList());
    }

    [Fact]
    public void A_user_only_sees_their_own_shop_row()
    {
        using var asA = NewContext(_shopA);

        var shop = Assert.Single(asA.Shops.ToList());
        Assert.Equal("Shop A", shop.Name);
    }

    [Fact]
    public void New_rows_get_the_current_shops_id()
    {
        using var asA = NewContext(_shopA);
        var note = new Note { Text = "new" };
        asA.Notes.Add(note);
        asA.SaveChanges();

        Assert.Equal(_shopA, note.ShopId);
    }

    [Fact]
    public void Saving_a_row_for_another_shop_is_refused()
    {
        using var asA = NewContext(_shopA);
        asA.Notes.Add(new Note { Text = "sneaky", ShopId = _shopB });

        Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
    }

    [Fact]
    public void Saving_shop_data_without_a_shop_is_refused()
    {
        using var noShop = NewContext(shopId: null);
        noShop.Notes.Add(new Note { Text = "orphan" });

        Assert.Throws<InvalidOperationException>(() => noShop.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();
}
