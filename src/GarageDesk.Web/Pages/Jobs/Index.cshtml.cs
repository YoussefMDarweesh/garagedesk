using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Pages.Jobs;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public const int PageSize = 100;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Optional status filter. Anything that isn't a status name means "all".</summary>
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public JobStatus? StatusFilter =>
        Enum.TryParse<JobStatus>(Status, ignoreCase: true, out var status) && Enum.IsDefined(status) ? status : null;

    public List<JobCard> Jobs { get; private set; } = [];

    public bool HasMore { get; private set; }

    public async Task OnGetAsync()
    {
        var jobs = db.JobCards.AsNoTracking().Search(Q);

        if (StatusFilter is JobStatus status)
        {
            jobs = jobs.Where(j => j.Status == status);
        }

        // Totals are worked out in memory from the lines (see JobCard.Total), so the lines are loaded too.
        var found = await jobs
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.DateIn).ThenByDescending(j => j.Id)
            .Take(PageSize + 1)
            .ToListAsync();

        HasMore = found.Count > PageSize;
        Jobs = found.Take(PageSize).ToList();
    }
}
