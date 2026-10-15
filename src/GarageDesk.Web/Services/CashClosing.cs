using GarageDesk.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>What came in and went out on one business day, worked out from stored payments and expenses.</summary>
public sealed record CashCloseFigures(DateOnly Date, MethodTotals Received, decimal ExpensesTotal, decimal CashExpenses)
{
    /// <summary>Opening cash + cash received - cash expenses, to the piastre.</summary>
    public decimal Expected(decimal openingCash) => JobTotals.Round(openingCash + Received.Cash - CashExpenses);
}

/// <summary>
/// The daily cash close. A day's figures are the payments received on that date (by method) and the expenses dated
/// that day. Expected cash in the drawer = the cash already there at the start (typed by the owner) + cash payments
/// received - expenses paid from the drawer. The owner types what they counted; the difference is counted minus
/// expected. Saved closes are immutable snapshots.
/// </summary>
public static class CashClosing
{
    public static async Task<CashCloseFigures> FiguresAsync(ApplicationDbContext db, DateOnly date)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidOn == date)
            .Select(p => new { p.Method, p.Amount })
            .ToListAsync();
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date == date)
            .Select(e => new { e.Amount, e.PaidFromCash })
            .ToListAsync();

        return new CashCloseFigures(
            date,
            MethodTotals.From(payments.Select(p => (p.Method, p.Amount))),
            expenses.Sum(e => e.Amount),
            expenses.Where(e => e.PaidFromCash).Sum(e => e.Amount));
    }

    /// <summary>
    /// A starting point for "cash already in the drawer": what was counted at the latest earlier close. The owner can
    /// change it (money may have been banked since). Zero when there is no earlier close.
    /// </summary>
    public static async Task<decimal> SuggestedOpeningAsync(ApplicationDbContext db, DateOnly date)
    {
        var previous = await db.CashCloses.AsNoTracking()
            .Where(c => c.Date < date)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(c => new { c.CountedCash })
            .FirstOrDefaultAsync();
        return previous?.CountedCash ?? 0m;
    }
}
