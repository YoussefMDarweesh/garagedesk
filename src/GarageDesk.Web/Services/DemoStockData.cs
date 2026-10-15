using GarageDesk.Web.Data;
using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

/// <summary>
/// The v1.4 part of the demo shop: a parts catalog with a ledger that adds up (some parts running low), job lines
/// that point at catalog parts with their cost, a few months of expenses and the last days' cash closes. Everything
/// is relative to "today", like the rest of the demo data.
/// </summary>
internal static class DemoStockData
{
    private sealed record Item(string Name, string Sku, decimal Cost, decimal Sell, decimal Threshold, decimal Remaining, string Supplier);

    // Names match the lines of the service packages in DemoData, so history lines link to the catalog.
    private static readonly Item[] Items =
    [
        new("Engine oil 5W-30 (4 L)", "OIL-5W30-4L", 480m, 650m, 8, 14, "Delta Lubricants"),
        new("Oil filter", "OF-100", 70m, 120m, 6, 2, "Cairo Auto Parts"),
        new("Front brake pads", "BP-F-200", 620m, 900m, 3, 1, "Cairo Auto Parts"),
        new("Brake discs (pair)", "BD-F-210", 1_350m, 1_800m, 2, 3, "Cairo Auto Parts"),
        new("AC gas R134a", "AC-134", 220m, 350m, 3, 5, "Cool Tech"),
        new("Cabin filter", "CF-300", 110m, 180m, 4, 6, "Cairo Auto Parts"),
        new("Spark plugs", "SP-400", 120m, 180m, 8, 12, "Cairo Auto Parts"),
        new("Battery 70Ah", "BAT-70", 1_850m, 2_400m, 2, 3, "Power Cells"),
        new("Coolant (5 L)", "CL-5", 190m, 280m, 3, 4, "Delta Lubricants"),
        new("Thermostat", "TH-500", 240m, 350m, 2, 1, "Cairo Auto Parts"),
    ];

    private static DateTime At(DateOnly day, int hour) =>
        DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

    public static async Task AddAsync(
        ApplicationDbContext db, DateOnly today, IReadOnlyList<JobCard> jobs, Estimate quote, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var parts = Items.ToDictionary(i => i.Name, i => new Part
        {
            Name = i.Name, Sku = i.Sku, Unit = "pcs", CostPrice = i.Cost, SellPrice = i.Sell, LowStockThreshold = i.Threshold,
            Supplier = i.Supplier, IsActive = true, CreatedAtUtc = now,
        });
        db.Parts.AddRange(parts.Values);

        // Ids are needed to point lines at parts, so what has been added so far is saved here.
        await db.SaveChangesAsync(cancellationToken);

        // Job lines that match a catalog part now point at it and remember its cost; the ledger gets a "used" row each.
        var uses = parts.Keys.ToDictionary(k => k, _ => new List<(DateTime At, JobCard Job, decimal Quantity)>());
        foreach (var job in jobs)
        {
            foreach (var line in job.Lines.Where(l => l.Type == JobLineType.Part && parts.ContainsKey(l.Description)))
            {
                var part = parts[line.Description];
                line.PartId = part.Id;
                line.UnitCost = part.CostPrice;
                uses[line.Description].Add((Min(At(job.DateIn, 8), now), job, line.Quantity));
            }
        }

        // The quote's catalog lines are quoted from the same parts (they take stock only when it becomes a job card).
        foreach (var line in quote.Lines.Where(l => l.Type == JobLineType.Part))
        {
            var catalogName = line.Description.StartsWith("Front brake discs") ? "Brake discs (pair)" : line.Description;
            if (parts.TryGetValue(catalogName, out var part))
            {
                line.PartId = part.Id;
            }
        }

        var purchasedValue = 0m;
        foreach (var item in Items)
        {
            var part = parts[item.Name];
            var used = uses[item.Name].OrderBy(u => u.At).ToList();
            var totalUsed = used.Sum(u => u.Quantity);

            // Opening stock covers every use, then one restock leaves the shelf at the planned quantity.
            var movements = new List<(DateTime At, StockMovementKind Kind, decimal Change, JobCard? Job)>
            {
                (At(today.AddDays(-420), 7), StockMovementKind.Opening, totalUsed, null),
                (At(today.AddDays(-30), 9), StockMovementKind.Purchase, item.Remaining, null),
            };
            movements.AddRange(used.Select(u => (u.At, StockMovementKind.JobUse, -u.Quantity, (JobCard?)u.Job)));
            purchasedValue += item.Remaining * item.Cost;

            var running = 0m;
            foreach (var (at, kind, change, job) in movements.OrderBy(m => m.At).ThenBy(m => m.Kind))
            {
                running += change;
                db.StockMovements.Add(new StockMovement
                {
                    Part = part,
                    Kind = kind,
                    QuantityChange = change,
                    QuantityAfter = running,
                    UnitCost = kind == StockMovementKind.Purchase ? item.Cost : null,
                    JobCard = job,
                    Note = kind == StockMovementKind.Opening ? "Opening stock" : "",
                    UserName = "Demo owner",
                    AtUtc = at,
                });
            }

            part.QuantityOnHand = running;
        }

        // Expenses for this month and the three before it.
        var categories = ExpenseCategory.Defaults.ToDictionary(n => n, n => new ExpenseCategory { Name = n });
        db.ExpenseCategories.AddRange(categories.Values);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        for (var back = 3; back >= 0; back--)
        {
            var month = thisMonth.AddMonths(-back);
            void Spend(string category, int day, decimal amount, string note, bool cash)
            {
                var date = month.AddDays(Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
                if (date > today)
                {
                    return;
                }

                db.Expenses.Add(new Expense
                {
                    Date = date, Category = categories[category], Amount = amount, Note = note, PaidFromCash = cash,
                    CreatedByName = "Demo owner", CreatedAtUtc = At(date, 12),
                });
            }

            Spend("Rent", 1, 5_000m, "Workshop rent", cash: false);
            Spend("Utilities", 10, 780m + back * 35m, "Electricity and water", cash: true);
            Spend("Other", 15, 150m, "Cleaning supplies", cash: true);
            Spend("Salaries", 28, 14_500m, "Staff salaries", cash: false);
        }

        // The restock of 30 days ago was paid from the drawer: one expense for what it cost.
        db.Expenses.Add(new Expense
        {
            Date = today.AddDays(-30), Category = categories["Parts purchase"], Amount = JobTotals.Round(purchasedValue),
            Note = "Restock from suppliers", PaidFromCash = true, CreatedByName = "Demo owner", CreatedAtUtc = At(today.AddDays(-30), 9),
        });

        // The last three days were closed. Yesterday the drawer was 20 EGP short.
        var payments = db.ChangeTracker.Entries<Payment>().Select(e => e.Entity).ToList();
        var expenses = db.ChangeTracker.Entries<Expense>().Select(e => e.Entity).ToList();
        var opening = 1_000m;
        for (var back = 3; back >= 1; back--)
        {
            var day = today.AddDays(-back);
            var received = MethodTotals.From(payments.Where(p => p.PaidOn == day).Select(p => (p.Method, p.Amount)));
            var dayExpenses = expenses.Where(e => e.Date == day).ToList();
            var cashExpenses = dayExpenses.Where(e => e.PaidFromCash).Sum(e => e.Amount);
            var expected = JobTotals.Round(opening + received.Cash - cashExpenses);
            var counted = back == 1 ? expected - 20m : expected;

            db.CashCloses.Add(new CashClose
            {
                Date = day, OpeningCash = opening, CashReceived = received.Cash, CardReceived = received.Card,
                TransferReceived = received.Transfer, WalletReceived = received.Wallet,
                ExpensesTotal = dayExpenses.Sum(e => e.Amount), CashExpenses = cashExpenses,
                ExpectedCash = expected, CountedCash = counted, Difference = counted - expected,
                Note = back == 1 ? "Change was given twice on one invoice." : null,
                ClosedByName = "Demo owner", ClosedAtUtc = At(day, 18),
            });
            opening = counted;
        }
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
