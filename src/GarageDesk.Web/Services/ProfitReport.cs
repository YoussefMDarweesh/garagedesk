using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

public sealed record MethodTotals(decimal Cash, decimal Card, decimal Transfer, decimal Wallet)
{
    public decimal Total => Cash + Card + Transfer + Wallet;

    public static MethodTotals From(IEnumerable<(PaymentMethod Method, decimal Amount)> payments)
    {
        decimal cash = 0, card = 0, transfer = 0, wallet = 0;
        foreach (var (method, amount) in payments)
        {
            switch (method)
            {
                case PaymentMethod.Card: card += amount; break;
                case PaymentMethod.BankTransfer: transfer += amount; break;
                case PaymentMethod.Wallet: wallet += amount; break;
                default: cash += amount; break;
            }
        }

        return new MethodTotals(cash, card, transfer, wallet);
    }
}

public sealed record JobProfitRow(int JobId, string? InvoiceLabel, DateOnly DateIn, string Plate, decimal Revenue, decimal PartCost, int PartLinesWithoutCost)
{
    public decimal Profit => Revenue - PartCost;
}

public sealed record ExpenseByCategory(string Category, decimal Total, int Count);

/// <summary>One month of the owner's profit report.</summary>
public sealed record MonthProfit(
    DateOnly Month,
    MethodTotals Received,
    decimal ExpensesTotal,
    IReadOnlyList<ExpenseByCategory> ExpensesByCategory,
    IReadOnlyList<JobProfitRow> Jobs)
{
    /// <summary>The cash-basis profit: payments received in the month minus expenses paid in the month.</summary>
    public decimal CashProfit => Received.Total - ExpensesTotal;

    public decimal JobRevenue => Jobs.Sum(j => j.Revenue);

    public decimal JobPartCost => Jobs.Sum(j => j.PartCost);

    public decimal JobProfit => JobRevenue - JobPartCost;
}

/// <summary>
/// The profit definitions, in one place so the report, the dashboard card and the export can never disagree.
///
/// Monthly profit is on a cash basis: the payments received in the month (by the day they were received) minus the
/// expenses paid in the month (by their date). It does not matter when the car came in.
///
/// Job profit is per job card: revenue (subtotal minus discount, before VAT) minus the cost of the catalog parts
/// used. The job profit table of a month lists the Paid job cards whose visit date is in that month.
/// </summary>
public static class ProfitReport
{
    /// <summary>"yyyy-MM" to the first day of that month; anything else (or a silly year) means the current month.</summary>
    public static DateOnly ParseMonth(string? key, DateOnly today) =>
        DateOnly.TryParseExact((key ?? "") + "-01", "yyyy-MM-dd", out var parsed) && parsed.Year >= 2000 && parsed <= today.AddYears(1)
            ? new DateOnly(parsed.Year, parsed.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);

    /// <summary>Only the two cash-basis numbers (the dashboard card): received and spent.</summary>
    public static async Task<(MethodTotals Received, decimal Expenses)> CashAsync(ApplicationDbContext db, DateOnly first, DateOnly nextMonth)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= first && p.PaidOn < nextMonth)
            .Select(p => new { p.Method, p.Amount })
            .ToListAsync();
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= first && e.Date < nextMonth)
            .Select(e => e.Amount)
            .ToListAsync();

        return (MethodTotals.From(payments.Select(p => (p.Method, p.Amount))), expenses.Sum());
    }

    /// <summary>
    /// The cash-basis profit of each of the last <paramref name="months"/> months, oldest first, ending with the month that
    /// contains <paramref name="today"/>. Same definition as the card (received minus expenses), two queries in all.
    /// Used for the dashboard's small trend line and the "vs last month" badge.
    /// </summary>
    public static async Task<List<decimal>> MonthlyProfitAsync(ApplicationDbContext db, DateOnly today, int months)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var first = thisMonth.AddMonths(-(months - 1));
        var end = thisMonth.AddMonths(1);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidOn >= first && p.PaidOn < end)
            .Select(p => new { p.PaidOn, p.Amount })
            .ToListAsync();
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= first && e.Date < end)
            .Select(e => new { e.Date, e.Amount })
            .ToListAsync();

        var result = new List<decimal>();
        for (var m = first; m < end; m = m.AddMonths(1))
        {
            result.Add(payments.Where(p => p.PaidOn.Year == m.Year && p.PaidOn.Month == m.Month).Sum(p => p.Amount)
                       - expenses.Where(e => e.Date.Year == m.Year && e.Date.Month == m.Month).Sum(e => e.Amount));
        }
        return result;
    }

    public static async Task<MonthProfit> MonthAsync(ApplicationDbContext db, DateOnly firstOfMonth)
    {
        var next = firstOfMonth.AddMonths(1);
        var (received, _) = await CashAsync(db, firstOfMonth, next);

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= firstOfMonth && e.Date < next)
            .Select(e => new { e.Amount, Category = e.Category!.Name })
            .ToListAsync();
        var byCategory = expenses.GroupBy(e => e.Category)
            .Select(g => new ExpenseByCategory(g.Key, g.Sum(e => e.Amount), g.Count()))
            .OrderByDescending(c => c.Total).ThenBy(c => c.Category)
            .ToList();

        var jobs = await db.JobCards.AsNoTracking()
            .Where(j => j.Status == JobStatus.Paid && j.DateIn >= firstOfMonth && j.DateIn < next)
            .Include(j => j.Vehicle)
            .Include(j => j.Lines)
            .OrderBy(j => j.DateIn).ThenBy(j => j.Id)
            .ToListAsync();
        var rows = jobs.Select(j => new JobProfitRow(
            j.Id, j.InvoiceLabel, j.DateIn, j.Vehicle?.Plate ?? "",
            JobProfit.Revenue(j), JobProfit.PartCost(j.Lines), JobProfit.PartLinesWithoutCost(j.Lines))).ToList();

        return new MonthProfit(firstOfMonth, received, expenses.Sum(e => e.Amount), byCategory, rows);
    }
}
