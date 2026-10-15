using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// Rule 2 in CLAUDE.md: every new shop-owned entity gets a tenant-isolation test. This one is ShareLink's.
public sealed class ShareLinkIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;
    private readonly int _linkA;
    private readonly byte[] _hashA = ShareTokens.Hash(ShareTokens.Generate());

    public ShareLinkIsolationTests()
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
            var link = new ShareLink
            {
                Kind = ShareLinkKind.Invoice, TargetId = 1, TokenHash = _hashA,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            };
            asA.ShareLinks.Add(link);
            asA.SaveChanges();
            _linkA = link.Id;
        }

        using var asB = NewContext(_shopB);
        asB.ShareLinks.Add(new ShareLink
        {
            Kind = ShareLinkKind.Status, TargetId = 1, TokenHash = ShareTokens.Hash(ShareTokens.Generate()),
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        });
        asB.SaveChanges();
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_sees_its_own_links()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal([ShareLinkKind.Invoice], asA.ShareLinks.Select(l => l.Kind).ToList());
        Assert.Equal([ShareLinkKind.Status], asB.ShareLinks.Select(l => l.Kind).ToList());
        Assert.Null(asB.ShareLinks.FirstOrDefault(l => l.Id == _linkA));
        Assert.Empty(asB.ShareLinks.Where(l => l.TokenHash == _hashA).ToList());
    }

    [Fact]
    public void Nobody_signed_in_sees_no_links_unless_the_filter_is_deliberately_ignored()
    {
        using var nobody = NewContext(null);

        Assert.Empty(nobody.ShareLinks.ToList());
        // The public page does exactly this, and only by token hash.
        Assert.NotNull(nobody.ShareLinks.IgnoreQueryFilters().FirstOrDefault(l => l.TokenHash == _hashA));
    }

    [Fact]
    public void A_shop_cannot_write_a_link_for_another_shop_and_new_links_get_the_current_shop()
    {
        using (var asA = NewContext(_shopA))
        {
            asA.ShareLinks.Add(new ShareLink { ShopId = _shopB, Kind = ShareLinkKind.Invoice, TargetId = 1, TokenHash = ShareTokens.Hash(ShareTokens.Generate()) });
            Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
        }

        using var again = NewContext(_shopA);
        var link = new ShareLink { Kind = ShareLinkKind.Estimate, TargetId = 5, TokenHash = ShareTokens.Hash(ShareTokens.Generate()) };
        again.ShareLinks.Add(link);
        again.SaveChanges();
        Assert.Equal(_shopA, link.ShopId);
    }

    [Fact]
    public void A_token_hash_is_unique()
    {
        using var asB = NewContext(_shopB);
        asB.ShareLinks.Add(new ShareLink { Kind = ShareLinkKind.Invoice, TargetId = 9, TokenHash = _hashA });

        Assert.Throws<DbUpdateException>(() => asB.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();
}
