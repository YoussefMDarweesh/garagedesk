using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Money;

/// <summary>The "add an expense" form.</summary>
public class ExpenseInput
{
    [Display(Name = "Date")]
    public DateOnly Date { get; set; }

    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Range(0.01, 10_000_000)]
    [Display(Name = "Amount (EGP)")]
    public decimal Amount { get; set; }

    [StringLength(200)]
    [Display(Name = "Note")]
    public string? Note { get; set; }

    [Display(Name = "Paid in cash from the drawer")]
    public bool PaidFromCash { get; set; } = true;
}

public sealed record ExpenseRow(int Id, DateOnly Date, string Category, decimal Amount, string? Note, bool PaidFromCash, string By);

public sealed record CategoryTotal(string Category, decimal Total, int Count);

/// <summary>Expenses by month, with totals by category. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class ExpensesModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>The month as "yyyy-MM". Anything else means the current month.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }

    [BindProperty]
    public ExpenseInput Input { get; set; } = new();

    public DateOnly MonthStart { get; private set; }

    public string PreviousKey => MonthStart.AddMonths(-1).ToString("yyyy-MM");

    public string NextKey => MonthStart.AddMonths(1).ToString("yyyy-MM");

    public List<ExpenseRow> Rows { get; private set; } = [];

    public List<CategoryTotal> Totals { get; private set; } = [];

    public decimal Total => Rows.Sum(r => r.Amount);

    public List<ExpenseCategory> Categories { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string CategoryName(string name) => ExpenseCategories.Display(localizer, name);

    public async Task OnGetAsync()
    {
        Input.Date = ShopClock.Today();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var today = ShopClock.Today();
        var categories = await ExpenseCategories.EnsureDefaultsAsync(db);
        var category = categories.FirstOrDefault(c => c.Id == Input.CategoryId && c.IsActive);

        if (category is null)
        {
            ModelState.AddModelError("Input.CategoryId", localizer["Choose a category."]);
        }

        if (Input.Date == default || Input.Date.Year < 2000 || Input.Date > today.AddYears(1))
        {
            ModelState.AddModelError("Input.Date", localizer["Enter a valid date."]);
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(Input.Date != default ? new DateOnly(Input.Date.Year, Input.Date.Month, 1) : null);
            return Page();
        }

        var actor = await currentStaff.GetAsync();
        db.Expenses.Add(new Expense
        {
            Date = Input.Date,
            CategoryId = category!.Id,
            Amount = JobTotals.Round(Input.Amount),
            Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim(),
            PaidFromCash = Input.PaidFromCash,
            CreatedByUserId = actor?.UserId,
            CreatedByName = actor?.DisplayName ?? "",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        StatusMessage = localizer["Expense saved."];
        return RedirectToPage(new { month = $"{Input.Date.Year:0000}-{Input.Date.Month:00}" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string? month)
    {
        // Another shop's expense id finds nothing (query filter), so it is a plain 404.
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null)
        {
            return NotFound();
        }

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        StatusMessage = localizer["Expense removed."];
        return RedirectToPage(new { month });
    }

    private async Task LoadAsync(DateOnly? forceMonth = null)
    {
        var today = ShopClock.Today();
        MonthStart = forceMonth
            ?? (DateOnly.TryParseExact((Month ?? "") + "-01", "yyyy-MM-dd", out var parsed) && parsed.Year >= 2000 && parsed <= today.AddYears(1)
                ? new DateOnly(parsed.Year, parsed.Month, 1)
                : new DateOnly(today.Year, today.Month, 1));
        var next = MonthStart.AddMonths(1);

        Categories = (await ExpenseCategories.EnsureDefaultsAsync(db)).Where(c => c.IsActive).ToList();
        if (Input.CategoryId == 0 && Categories.Count > 0)
        {
            Input.CategoryId = Categories[0].Id;
        }

        var rows = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= MonthStart && e.Date < next)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.Date, Category = e.Category!.Name, e.Amount, e.Note, e.PaidFromCash, e.CreatedByName })
            .ToListAsync();

        Rows = rows.Select(r => new ExpenseRow(r.Id, r.Date, r.Category, r.Amount, r.Note, r.PaidFromCash, r.CreatedByName)).ToList();
        Totals = Rows.GroupBy(r => r.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(r => r.Amount), g.Count()))
            .OrderByDescending(t => t.Total).ThenBy(t => t.Category)
            .ToList();
    }
}
