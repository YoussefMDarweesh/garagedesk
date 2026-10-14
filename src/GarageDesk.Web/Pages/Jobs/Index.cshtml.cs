using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Jobs;

[Authorize(Policy = Policies.Staff)]
public class IndexModel(ApplicationDbContext db, ICurrentStaff currentStaff) : PageModel
{
    public const int PageSize = 20;

    /// <summary>"me" (my jobs), "none" (unassigned) or a team member's id. Anything else means everybody.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Assigned { get; set; }

    public bool MineOnly => string.Equals(Assigned, "me", StringComparison.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Optional status filter. Anything that isn't a status name means "all".</summary>
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public JobStatus? StatusFilter =>
        Enum.TryParse<JobStatus>(Status, ignoreCase: true, out var status) && Enum.IsDefined(status) ? status : null;

    public List<JobCard> Jobs { get; private set; } = [];

    public int Total { get; private set; }

    public int PageCount { get; private set; } = 1;

    public PagerModel Pager => new(P, PageCount, Total, PageSize, "/Jobs/Index",
        new Dictionary<string, string?> { ["q"] = Q, ["status"] = StatusFilter?.ToString(), ["assigned"] = Assigned });

    public async Task OnGetAsync()
    {
        var query = db.JobCards.AsNoTracking().Search(Q);

        if (MineOnly)
        {
            var me = (await currentStaff.GetAsync())!.UserId;
            query = query.Where(j => j.AssignedToUserId == me);
        }
        else if (string.Equals(Assigned, "none", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(j => j.AssignedToUserId == null);
        }
        else if (!string.IsNullOrWhiteSpace(Assigned))
        {
            var who = Assigned;
            query = query.Where(j => j.AssignedToUserId == who); // the shop filter already limits this to this shop's jobs
        }

        if (StatusFilter is JobStatus status)
        {
            query = query.Where(j => j.Status == status);
        }

        Total = await query.CountAsync();
        PageCount = PagerModel.CountPages(Total, PageSize);
        P = Math.Clamp(P, 1, PageCount);

        // Totals are worked out in memory from the lines (see JobCard.Total), so the lines are loaded too.
        Jobs = await query
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .Include(j => j.AssignedTo)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
