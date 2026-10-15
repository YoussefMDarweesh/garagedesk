using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Money;

/// <summary>Download payments, expenses or job cards for a date range as a CSV file. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class ExportModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>The longest range one file may cover, to keep a download a sensible size.</summary>
    public const int MaxDays = 366;

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    public string FromText { get; private set; } = "";

    public string ToText { get; private set; } = "";

    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        var today = ShopClock.Today();
        var from = Parse(From) ?? new DateOnly(today.Year, today.Month, 1);
        var to = Parse(To) ?? today;
        FromText = CsvExport.Date(from);
        ToText = CsvExport.Date(to);
    }

    /// <summary>A read-only GET that answers with a file. Dates come from the query string and are checked here.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(string? kind)
    {
        var from = Parse(From);
        var to = Parse(To);
        if (from is null || to is null || to < from || to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
        {
            ErrorMessage = localizer["Choose a start and an end date, at most 366 days apart."];
            return RedirectToPage();
        }

        var (name, headers, rows) = kind switch
        {
            "payments" => await PaymentsAsync(from.Value, to.Value),
            "expenses" => await ExpensesAsync(from.Value, to.Value),
            "jobs" => await JobsAsync(from.Value, to.Value),
            _ => default,
        };
        if (name is null)
        {
            return BadRequest();
        }

        Response.Headers.CacheControl = "no-store";
        return File(CsvExport.Build(headers!, rows!), "text/csv; charset=utf-8",
            $"garagedesk-{name}-{CsvExport.Date(from.Value)}-{CsvExport.Date(to.Value)}.csv");
    }

    private static DateOnly? Parse(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", out var date) && date.Year >= 2000 && date.Year <= 2100 ? date : null;

    private string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => localizer["Card"].Value,
        PaymentMethod.BankTransfer => localizer["Bank transfer"].Value,
        PaymentMethod.Wallet => localizer["Wallet"].Value,
        _ => localizer["Cash"].Value,
    };

    private string StatusName(JobStatus status) => status switch
    {
        JobStatus.InProgress => localizer["In progress"].Value,
        JobStatus.Done => localizer["Done"].Value,
        JobStatus.Paid => localizer["Paid"].Value,
        _ => localizer["Booked"].Value,
    };

    private async Task<(string?, string[]?, List<string[]>?)> PaymentsAsync(DateOnly from, DateOnly to)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Include(p => p.JobCard).ThenInclude(j => j!.Vehicle).ThenInclude(v => v!.Customer)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.Id)
            .ToListAsync();

        string[] headers =
        [
            localizer["Date"].Value, localizer["Invoice number"].Value, localizer["Job card"].Value, localizer["Customer"].Value,
            localizer["Plate number"].Value, localizer["Payment method"].Value, localizer["Amount (EGP)"].Value, localizer["Note"].Value,
        ];
        var rows = payments.Select(p => new[]
        {
            CsvExport.Date(p.PaidOn),
            CsvExport.Text(p.JobCard?.InvoiceLabel),
            p.JobCardId.ToString(),
            CsvExport.Text(p.JobCard?.Vehicle?.Customer?.Name),
            CsvExport.Text(p.JobCard?.Vehicle?.Plate),
            CsvExport.Text(MethodName(p.Method)),
            CsvExport.Number(p.Amount),
            CsvExport.Text(p.Note),
        }).ToList();
        return ("payments", headers, rows);
    }

    private async Task<(string?, string[]?, List<string[]>?)> ExpensesAsync(DateOnly from, DateOnly to)
    {
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .Select(e => new { e.Date, Category = e.Category!.Name, e.Amount, e.PaidFromCash, e.Note, e.CreatedByName, e.Id })
            .ToListAsync();

        string[] headers =
        [
            localizer["Date"].Value, localizer["Category"].Value, localizer["Amount (EGP)"].Value,
            localizer["Paid in cash from the drawer"].Value, localizer["Note"].Value, localizer["Entered by"].Value,
        ];
        var rows = expenses.OrderBy(e => e.Date).ThenBy(e => e.Id).Select(e => new[]
        {
            CsvExport.Date(e.Date),
            CsvExport.Text(ExpenseCategories.Display(localizer, e.Category)),
            CsvExport.Number(e.Amount),
            CsvExport.Text(e.PaidFromCash ? localizer["Yes"].Value : localizer["No"].Value),
            CsvExport.Text(e.Note),
            CsvExport.Text(e.CreatedByName),
        }).ToList();
        return ("expenses", headers, rows);
    }

    private async Task<(string?, string[]?, List<string[]>?)> JobsAsync(DateOnly from, DateOnly to)
    {
        var jobs = await db.JobCards.AsNoTracking()
            .Where(j => j.DateIn >= from && j.DateIn <= to)
            .Include(j => j.Vehicle).ThenInclude(v => v!.Customer)
            .Include(j => j.Lines)
            .Include(j => j.Payments)
            .OrderBy(j => j.DateIn).ThenBy(j => j.Id)
            .ToListAsync();

        string[] headers =
        [
            localizer["Job card"].Value, localizer["Invoice number"].Value, localizer["Visit date"].Value, localizer["Status"].Value,
            localizer["Customer"].Value, localizer["Plate number"].Value, localizer["Parts"].Value, localizer["Labor"].Value,
            localizer["Discount"].Value, localizer["VAT"].Value, localizer["Total"].Value, localizer["Received"].Value,
            localizer["Balance due"].Value, localizer["Part cost"].Value, localizer["Profit"].Value,
        ];
        var rows = jobs.Select(j => new[]
        {
            j.Id.ToString(),
            CsvExport.Text(j.InvoiceLabel),
            CsvExport.Date(j.DateIn),
            CsvExport.Text(StatusName(j.Status)),
            CsvExport.Text(j.Vehicle?.Customer?.Name),
            CsvExport.Text(j.Vehicle?.Plate),
            CsvExport.Number(j.PartsTotal),
            CsvExport.Number(j.LaborTotal),
            CsvExport.Number(j.DiscountValue),
            CsvExport.Number(j.VatValue),
            CsvExport.Number(j.Total),
            CsvExport.Number(j.PaidTotal),
            CsvExport.Number(j.Balance),
            CsvExport.Number(JobProfit.PartCost(j.Lines)),
            CsvExport.Number(JobProfit.Of(j)),
        }).ToList();
        return ("jobs", headers, rows);
    }
}
