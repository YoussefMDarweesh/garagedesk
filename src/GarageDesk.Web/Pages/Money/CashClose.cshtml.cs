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

/// <summary>What the owner types when closing the day. The figures themselves are never posted: the server works them out.</summary>
public class CashCloseInput
{
    [Range(0, 100_000_000)]
    [Display(Name = "Cash already in the drawer at the start of the day")]
    public decimal OpeningCash { get; set; }

    [Range(0, 100_000_000)]
    [Display(Name = "Cash you counted now")]
    public decimal CountedCash { get; set; }

    [StringLength(200)]
    [Display(Name = "Note")]
    public string? Note { get; set; }
}

/// <summary>The daily cash close: the day's takings and expenses, the cash that should be in the drawer, and the count. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class CashCloseModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>The day as "yyyy-MM-dd". Anything else (or a future date) means today.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Date { get; set; }

    [BindProperty]
    public CashCloseInput Input { get; set; } = new();

    public DateOnly Day { get; private set; }

    public CashCloseFigures Figures { get; private set; } = null!;

    /// <summary>The closes already saved for this day, newest first. The first one is the one that counts.</summary>
    public List<CashClose> Closes { get; private set; } = [];

    /// <summary>The last few days that were closed, for a quick look back.</summary>
    public List<CashClose> Recent { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public decimal Expected => Figures.Expected(Input.OpeningCash);

    /// <summary>True when payments or expenses of the day changed after the newest close was saved.</summary>
    public bool ChangedSinceClose(CashClose close) =>
        close.Id == Closes.FirstOrDefault()?.Id
        && (close.CashReceived != Figures.Received.Cash || close.CardReceived != Figures.Received.Card
            || close.TransferReceived != Figures.Received.Transfer || close.WalletReceived != Figures.Received.Wallet
            || close.ExpensesTotal != Figures.ExpensesTotal || close.CashExpenses != Figures.CashExpenses);

    public async Task OnGetAsync()
    {
        await LoadAsync();
        Input.OpeningCash = Closes.Count > 0 ? Closes[0].OpeningCash : await CashClosing.SuggestedOpeningAsync(db, Day);
        Input.CountedCash = Closes.Count > 0 ? Closes[0].CountedCash : 0;
    }

    public async Task<IActionResult> OnPostCloseAsync()
    {
        await LoadAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var actor = await currentStaff.GetAsync();
        var opening = JobTotals.Round(Input.OpeningCash);
        var counted = JobTotals.Round(Input.CountedCash);
        var expected = Figures.Expected(opening);

        // The saved figures are the server's own, taken at this moment. Nothing but the three typed values comes from the form.
        db.CashCloses.Add(new CashClose
        {
            Date = Day,
            OpeningCash = opening,
            CashReceived = Figures.Received.Cash,
            CardReceived = Figures.Received.Card,
            TransferReceived = Figures.Received.Transfer,
            WalletReceived = Figures.Received.Wallet,
            ExpensesTotal = Figures.ExpensesTotal,
            CashExpenses = Figures.CashExpenses,
            ExpectedCash = expected,
            CountedCash = counted,
            Difference = JobTotals.Round(counted - expected),
            Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim(),
            ClosedByUserId = actor?.UserId,
            ClosedByName = actor?.DisplayName ?? "",
            ClosedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        StatusMessage = localizer["The day was closed."];
        return RedirectToPage(new { date = Day.ToString("yyyy-MM-dd") });
    }

    private async Task LoadAsync()
    {
        var today = ShopClock.Today();
        Day = DateOnly.TryParseExact(Date, "yyyy-MM-dd", out var parsed) && parsed >= new DateOnly(2000, 1, 1) && parsed <= today ? parsed : today;

        Figures = await CashClosing.FiguresAsync(db, Day);
        Closes = await db.CashCloses.AsNoTracking()
            .Where(c => c.Date == Day)
            .OrderByDescending(c => c.ClosedAtUtc).ThenByDescending(c => c.Id)
            .ToListAsync();
        Recent = await db.CashCloses.AsNoTracking()
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Take(10)
            .ToListAsync();
    }
}
