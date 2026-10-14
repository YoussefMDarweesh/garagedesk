using GarageDesk.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

// Rule 2 in CLAUDE.md: every new shop-owned entity gets a tenant-isolation test.
// These are JobCard's and JobLine's, at the DbContext level.
public sealed class JobCardIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _shopA;
    private readonly int _shopB;
    private readonly int _jobA;
    private readonly int _vehicleA;

    public JobCardIsolationTests()
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

        (_jobA, _vehicleA) = Seed(_shopA, "A");
        Seed(_shopB, "B");
    }

    private (int JobId, int VehicleId) Seed(int shopId, string tag)
    {
        using var db = NewContext(shopId);
        var customer = new Customer { Name = $"Customer {tag}", Phone = "+201000000000" };
        var vehicle = new Vehicle { Customer = customer, Plate = $"{tag} 1", PlateKey = $"{tag}1", Make = "Kia", Model = "Rio", Year = 2020 };
        var job = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 10, 1), Notes = $"Job {tag}" };
        job.Lines.Add(new JobLine { Type = JobLineType.Part, Description = $"Part {tag}", Quantity = 1, UnitPrice = 100 });
        db.JobCards.Add(job);
        db.SaveChanges();
        return (job.Id, vehicle.Id);
    }

    private TestDbContext NewContext(int? shopId) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options, new FakeCurrentShop(shopId));

    [Fact]
    public void Each_shop_only_sees_its_own_job_cards_and_lines()
    {
        using var asA = NewContext(_shopA);
        using var asB = NewContext(_shopB);

        Assert.Equal(["Job A"], asA.JobCards.Select(j => j.Notes).ToList());
        Assert.Equal(["Part A"], asA.JobLines.Select(l => l.Description).ToList());
        Assert.Equal(["Job B"], asB.JobCards.Select(j => j.Notes).ToList());
        Assert.Equal(["Part B"], asB.JobLines.Select(l => l.Description).ToList());
    }

    [Fact]
    public void A_shop_cannot_find_another_shops_job_card_by_id_or_through_its_lines()
    {
        using var asB = NewContext(_shopB);

        Assert.Null(asB.JobCards.FirstOrDefault(j => j.Id == _jobA));
        Assert.Empty(asB.JobLines.Where(l => l.JobCardId == _jobA).ToList());
        Assert.DoesNotContain(asB.JobCards.Include(j => j.Lines).ToList().SelectMany(j => j.Lines), l => l.JobCardId == _jobA);
    }

    [Fact]
    public void Nobody_signed_in_or_without_a_shop_sees_nothing()
    {
        using var noShop = NewContext(null);

        Assert.Empty(noShop.JobCards.ToList());
        Assert.Empty(noShop.JobLines.ToList());
    }

    [Fact]
    public void New_rows_get_the_current_shops_id()
    {
        using var asA = NewContext(_shopA);
        var job = new JobCard { VehicleId = _vehicleA, DateIn = new DateOnly(2026, 10, 2) };
        job.Lines.Add(new JobLine { Description = "Oil", Quantity = 1, UnitPrice = 10 });
        asA.JobCards.Add(job);
        asA.SaveChanges();

        Assert.Equal(_shopA, job.ShopId);
        Assert.Equal(_shopA, job.Lines.Single().ShopId);
    }

    [Fact]
    public void A_shop_cannot_write_job_cards_or_lines_for_another_shop()
    {
        using (var asA = NewContext(_shopA))
        {
            asA.JobCards.Add(new JobCard { ShopId = _shopB, VehicleId = _vehicleA, DateIn = new DateOnly(2026, 10, 2) });
            Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
        }

        using (var asA = NewContext(_shopA))
        {
            asA.JobLines.Add(new JobLine { ShopId = _shopB, JobCardId = _jobA, Description = "Sneaky", Quantity = 1, UnitPrice = 1 });
            Assert.Throws<InvalidOperationException>(() => asA.SaveChanges());
        }
    }

    [Fact]
    public void Status_and_enum_values_round_trip()
    {
        using (var asA = NewContext(_shopA))
        {
            var job = asA.JobCards.Single();
            job.Status = JobStatus.InProgress;
            asA.SaveChanges();
        }

        using var again = NewContext(_shopA);
        Assert.Equal(JobStatus.InProgress, again.JobCards.Single().Status);
        Assert.Equal(JobLineType.Part, again.JobLines.Single().Type);
    }

    public void Dispose() => _connection.Dispose();
}
