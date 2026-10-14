using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>The v1.3 queries must translate to PostgreSQL too (production), not just SQLite (tests). Nothing is executed.</summary>
public class TeamPostgresTranslationTests
{
    private static ApplicationDbContext NewPostgresContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .Options,
            new FakeCurrentShop(1));

    [Fact]
    public void My_jobs_projection_translates()
    {
        using var db = NewPostgresContext();

        var sql = db.JobCards.AsNoTracking()
            .Where(j => j.AssignedToUserId == "u1" && j.Status != JobStatus.Paid)
            .OrderByDescending(j => j.DateIn)
            .Select(j => new { j.Id, j.Vehicle!.Plate, Owner = j.Vehicle.Customer!.Name, Lines = j.Lines.Count })
            .Take(200)
            .ToQueryString();

        Assert.Contains("\"AssignedToUserId\"", sql);
        Assert.Contains("\"ShopId\"", sql);
        Assert.DoesNotContain("UnitPrice", sql); // the technician's list never even reads a price
        Assert.DoesNotContain("Phone", sql);
    }

    [Fact]
    public void The_commission_query_translates()
    {
        using var db = NewPostgresContext();
        var first = new DateOnly(2026, 3, 1);
        var next = first.AddMonths(1);

        var sql = db.JobCards.AsNoTracking()
            .Where(j => j.Status == JobStatus.Paid && j.AssignedToUserId != null && j.DateIn >= first && j.DateIn < next)
            .Select(j => new
            {
                j.AssignedToUserId,
                Labor = j.Lines.Where(l => l.Type == JobLineType.Labor).Select(l => new { l.Quantity, l.UnitPrice }).ToList(),
            })
            .ToQueryString();

        Assert.Contains("\"JobLines\"", sql);
        Assert.Contains("\"ShopId\"", sql);
    }

    [Fact]
    public void The_team_queries_translate_and_state_the_shop()
    {
        using var db = NewPostgresContext();

        var members = db.MembersOf(1).AsNoTracking().Where(u => u.IsActive && u.Role == ShopRole.Owner).ToQueryString();
        Assert.Contains("\"ShopId\"", members);
        Assert.Contains("\"Role\"", members);

        var invites = db.StaffInvites.AsNoTracking()
            .Where(i => i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow)
            .ToQueryString();
        Assert.Contains("\"ShopId\"", invites); // the tenant filter applies to invitations
    }
}
