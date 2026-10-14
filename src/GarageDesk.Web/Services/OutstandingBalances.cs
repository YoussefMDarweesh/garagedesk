using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>One customer and what they still owe across their finished, not fully paid job cards.</summary>
public sealed record CustomerBalance(Customer Customer, decimal Owed, int Jobs, DateOnly Oldest);

/// <summary>"Who owes me": finished (Done) job cards with a balance, worked out with the same JobCard.Balance code as every page.</summary>
public static class OutstandingBalances
{
    public static async Task<List<CustomerBalance>> LoadAsync(ApplicationDbContext db, int? customerId = null)
    {
        var query = db.JobCards.AsNoTracking()
            .Include(j => j.Lines).Include(j => j.Payments)
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Where(j => j.Status == JobStatus.Done);

        if (customerId is { } id)
        {
            query = query.Where(j => j.Vehicle!.CustomerId == id);
        }

        var jobs = (await query.ToListAsync()).Where(j => j.Balance > 0);

        return jobs
            .GroupBy(j => j.Vehicle!.CustomerId)
            .Select(g => new CustomerBalance(g.First().Vehicle!.Customer!, g.Sum(j => j.Balance), g.Count(), g.Min(j => j.DateIn)))
            .OrderBy(b => b.Oldest).ThenBy(b => b.Customer.Name)
            .ToList();
    }
}
