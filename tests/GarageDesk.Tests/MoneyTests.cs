using System.Net;
using System.Text;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>v1.4 parts 2 to 4: expenses, profit and the daily cash close, plus who may open them.</summary>
public class MoneyTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static readonly DateOnly March1 = new(2026, 3, 1);

    private async Task<int> AddPaymentAsync(int shop, int job, decimal amount, DateOnly on, PaymentMethod method = PaymentMethod.Cash)
    {
        using var db = factory.CreateContextFor(shop);
        var payment = new Payment { JobCardId = job, Amount = amount, Method = method, PaidOn = on };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<int> AddExpenseAsync(int shop, string category, decimal amount, DateOnly on, bool fromCash = true, string? note = null)
    {
        using var db = factory.CreateContextFor(shop);
        var categories = await ExpenseCategories.EnsureDefaultsAsync(db);
        var expense = new Expense
        {
            Date = on, CategoryId = categories.Single(c => c.Name == category).Id, Amount = amount, PaidFromCash = fromCash,
            Note = note, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    // ---------------------------------------------------------------- who may open the money pages

    [Theory]
    [InlineData("/Money/Profit")]
    [InlineData("/Money/Expenses")]
    [InlineData("/Money/Categories")]
    [InlineData("/Money/CashClose")]
    [InlineData("/Money/Export")]
    public async Task Only_the_owner_opens_the_money_pages_and_others_get_a_403(string path)
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Money Roles");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var tech = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientFor(owner, shop).GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(cashier, shop).GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientFor(tech, shop).GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_cashier_cannot_post_an_expense_close_the_day_or_download_an_export()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Money Cashier Post");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var client = factory.CreateClientFor(cashier, shop);

        var expense = await client.PostAsync("/Money/Expenses", new FormUrlEncodedContent([new("Input.Amount", "10")]));
        var close = await client.PostAsync("/Money/CashClose?handler=Close", new FormUrlEncodedContent([new("Input.CountedCash", "10")]));
        var export = await client.GetAsync("/Money/Export?handler=Download&kind=payments&from=2026-03-01&to=2026-03-31");

        Assert.Equal(HttpStatusCode.Forbidden, expense.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Expenses.ToListAsync());
        Assert.Empty(await db.CashCloses.ToListAsync());
    }

    [Fact]
    public async Task The_menu_shows_money_to_the_owner_only()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Money Menu");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);

        var ownerHtml = await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Customers"));
        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync("/Customers"));

        Assert.Contains("href=\"/Money/Profit\"", ownerHtml);
        Assert.DoesNotContain("/Money", cashierHtml);
    }

    // ---------------------------------------------------------------- expenses

    [Fact]
    public async Task Expenses_are_listed_by_month_with_totals_by_category()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Expenses Month");
        await AddExpenseAsync(shop, "Rent", 5000, new DateOnly(2026, 3, 2), note: "March rent");
        await AddExpenseAsync(shop, "Utilities", 320.55m, new DateOnly(2026, 3, 10));
        await AddExpenseAsync(shop, "Utilities", 100.10m, new DateOnly(2026, 3, 20));
        await AddExpenseAsync(shop, "Rent", 4000, new DateOnly(2026, 4, 2), note: "April rent");

        var html = (await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Money/Expenses?month=2026-03"))).Replace("\n", " ");

        Assert.Contains("March rent", html);
        Assert.DoesNotContain("April rent", html);
        Assert.Matches(@"data-category=""Utilities""[^>]*>.*?420\.65", html);
        Assert.Matches(@"data-category=""Rent""[^>]*>.*?5,000\.00", html);
        Assert.Matches(@"data-expenses-total[^>]*>.*?5,420\.65", html);
    }

    [Fact]
    public async Task An_expense_is_saved_with_the_default_categories_created_on_first_use()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Expenses Add");
        var client = factory.CreateClientFor(owner, shop);

        int rent;
        using (var db = factory.CreateContextFor(shop))
        {
            var categories = await ExpenseCategories.EnsureDefaultsAsync(db);
            Assert.Equal(["Rent", "Salaries", "Parts purchase", "Utilities", "Other"], categories.Select(c => c.Name).ToArray());
            rent = categories[0].Id;
        }

        var response = await TestData.SubmitFormAsync(client, "/Money/Expenses",
            ("Input.Date", "2026-03-05"), ("Input.CategoryId", rent.ToString()), ("Input.Amount", "1234.565"),
            ("Input.Note", "Shop rent"), ("Input.PaidFromCash", "true"), ("Input.PaidFromCash", "false"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verify = factory.CreateContextFor(shop);
        var expense = await verify.Expenses.SingleAsync();
        Assert.Equal(1234.57m, expense.Amount); // rounded half away from zero to the piastre
        Assert.Equal(new DateOnly(2026, 3, 5), expense.Date);
        Assert.Equal(rent, expense.CategoryId);
        Assert.True(expense.PaidFromCash);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task An_expense_needs_an_amount_above_zero(string amount)
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Expenses Bad " + amount);
        using (var db = factory.CreateContextFor(shop))
        {
            await ExpenseCategories.EnsureDefaultsAsync(db);
        }

        int category;
        using (var db = factory.CreateContextFor(shop))
        {
            category = (await db.ExpenseCategories.FirstAsync()).Id;
        }

        var response = await TestData.SubmitFormAsync(factory.CreateClientFor(owner, shop), "/Money/Expenses",
            ("Input.Date", "2026-03-05"), ("Input.CategoryId", category.ToString()), ("Input.Amount", amount));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the form is shown again
        using var verify = factory.CreateContextFor(shop);
        Assert.Empty(await verify.Expenses.ToListAsync());
    }

    [Fact]
    public async Task Another_shop_cannot_see_use_or_delete_our_expenses_and_categories()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Exp A");
        var (ownerB, shopB) = await TestData.CreateOwnerAsync(factory, "Exp B");
        var expenseA = await AddExpenseAsync(shopA, "Rent", 777.77m, March1, note: "Secret rent");
        int categoryA;
        using (var db = factory.CreateContextFor(shopA))
        {
            categoryA = (await db.ExpenseCategories.FirstAsync()).Id;
        }

        var clientB = factory.CreateClientFor(ownerB, shopB);
        Assert.DoesNotContain("Secret rent", await TestData.BodyAsync(await clientB.GetAsync("/Money/Expenses?month=2026-03")));

        // Deleting by id from the other shop: 404, and the row is still there.
        var delete = await StockTestData.PostAsync(clientB, "/Money/Expenses", $"/Money/Expenses?handler=Delete&id={expenseA}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // Filing a new expense of shop B under shop A's category is refused.
        await TestData.SubmitFormAsync(clientB, "/Money/Expenses",
            ("Input.Date", "2026-03-05"), ("Input.CategoryId", categoryA.ToString()), ("Input.Amount", "50"));

        using var dbA = factory.CreateContextFor(shopA);
        Assert.Single(await dbA.Expenses.ToListAsync());
        using var dbB = factory.CreateContextFor(shopB);
        Assert.Empty(await dbB.Expenses.ToListAsync());
        Assert.DoesNotContain(await dbB.ExpenseCategories.ToListAsync(), c => c.Id == categoryA);
    }

    [Fact]
    public async Task Categories_can_be_added_renamed_and_archived_and_names_stay_unique()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Categories");
        var client = factory.CreateClientFor(owner, shop);
        await client.GetAsync("/Money/Categories"); // creates the defaults

        await StockTestData.PostAsync(client, "/Money/Categories", "/Money/Categories?handler=Add", ("name", "Insurance"));
        await StockTestData.PostAsync(client, "/Money/Categories", "/Money/Categories?handler=Add", ("name", "insurance")); // same name, other case
        await StockTestData.PostAsync(client, "/Money/Categories", "/Money/Categories?handler=Add", ("name", "RENT"));

        int insuranceId;
        using (var db = factory.CreateContextFor(shop))
        {
            var all = await db.ExpenseCategories.ToListAsync();
            Assert.Equal(6, all.Count); // five defaults and one new
            insuranceId = all.Single(c => c.Name == "Insurance").Id;
        }

        await StockTestData.PostAsync(client, "/Money/Categories", $"/Money/Categories?handler=Rename&id={insuranceId}", ("name", "Insurance and tax"));
        await StockTestData.PostAsync(client, "/Money/Categories", $"/Money/Categories?handler=Active&id={insuranceId}&active=false");

        using (var db = factory.CreateContextFor(shop))
        {
            var renamed = await db.ExpenseCategories.SingleAsync(c => c.Id == insuranceId);
            Assert.Equal("Insurance and tax", renamed.Name);
            Assert.False(renamed.IsActive);
        }

        // An archived category is not offered for a new expense.
        var form = await TestData.BodyAsync(await client.GetAsync("/Money/Expenses"));
        Assert.DoesNotContain("Insurance and tax", form);
    }

    // ---------------------------------------------------------------- profit

    [Fact]
    public async Task Monthly_profit_is_payments_received_minus_expenses_whatever_the_visit_date()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Profit Month");
        // A job that came in during February but was paid in March counts in March.
        var febJob = await TestData.SeedJobAsync(factory, shop, JobStatus.Paid, dateIn: new DateOnly(2026, 2, 20), plate: "PRO 1");
        var marJob = await TestData.SeedJobAsync(factory, shop, JobStatus.Done, dateIn: new DateOnly(2026, 3, 3), plate: "PRO 2");
        await AddPaymentAsync(shop, febJob, 203.45m, new DateOnly(2026, 3, 1));
        await AddPaymentAsync(shop, marJob, 100.01m,new DateOnly(2026, 3, 31), PaymentMethod.Card);
        await AddPaymentAsync(shop, marJob, 50m, new DateOnly(2026, 4, 1)); // April: not in March
        await AddExpenseAsync(shop, "Rent", 120.25m, new DateOnly(2026, 3, 5));
        await AddExpenseAsync(shop, "Other", 30m, new DateOnly(2026, 4, 5)); // April

        using var db = factory.CreateContextFor(shop);
        var report = await ProfitReport.MonthAsync(db, March1);

        Assert.Equal(303.46m, report.Received.Total);
        Assert.Equal(203.45m, report.Received.Cash);
        Assert.Equal(100.01m, report.Received.Card);
        Assert.Equal(120.25m, report.ExpensesTotal);
        Assert.Equal(183.21m, report.CashProfit);

        var html = (await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Money/Profit?month=2026-03"))).Replace("\n", " ");
        Assert.Matches(@"data-cash-profit[^>]*>.*?183\.21", html);
        Assert.Contains("Cash basis", html);
        Assert.Contains("How profit is worked out", html);
    }

    [Fact]
    public async Task Job_profit_is_revenue_before_vat_minus_part_cost_rounded_half_away_from_zero()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Profit Job");
        int job;
        using (var db = factory.CreateContextFor(shop))
        {
            var customer = new Customer { Name = "Profit Customer", Phone = "+201000000066" };
            var vehicle = new Vehicle { Customer = customer, Plate = "PRF 1", PlateKey = "PRF1", Make = "Kia", Model = "Rio", Year = 2019, Mileage = 1 };
            var card = new JobCard { Vehicle = vehicle, DateIn = new DateOnly(2026, 3, 9), MileageAtVisit = 1, Status = JobStatus.Paid, DiscountPercent = 10, VatPercent = 14 };
            // part: 1.5 x 20.01 = 30.015 -> 30.02 revenue; cost 1.5 x 10.01 = 15.015 -> 15.02
            card.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Pads", Quantity = 1.5m, UnitPrice = 20.01m, UnitCost = 10.01m });
            card.Lines.Add(new JobLine { Type = JobLineType.Labor, Description = "Fit", Quantity = 1, UnitPrice = 100m });
            card.Lines.Add(new JobLine { Type = JobLineType.Part, Description = "Free text bolt", Quantity = 1, UnitPrice = 5m });
            db.JobCards.Add(card);
            await db.SaveChangesAsync();
            job = card.Id;
        }

        using var read = factory.CreateContextFor(shop);
        var loaded = await read.JobCards.Include(j => j.Lines).SingleAsync(j => j.Id == job);

        // subtotal 135.02, 10% discount = 13.50 (13.502), so revenue (before VAT) = 121.52; cost = 15.02
        Assert.Equal(135.02m, loaded.Subtotal);
        Assert.Equal(13.50m, loaded.DiscountValue);
        Assert.Equal(121.52m, JobProfit.Revenue(loaded));
        Assert.Equal(15.02m, JobProfit.PartCost(loaded.Lines));
        Assert.Equal(106.50m, JobProfit.Of(loaded));
        Assert.Equal(1, JobProfit.PartLinesWithoutCost(loaded.Lines));

        var html = (await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/Money/Profit?month=2026-03"))).Replace("\n", " ");
        Assert.Matches(@"data-job=""" + job + @"""[^>]*>.*?121\.52.*?15\.02.*?106\.50", html);
    }

    [Fact]
    public async Task The_dashboard_profit_card_is_the_owners_and_matches_the_report()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Profit Card");
        var cashier = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var today = ShopClock.Today();
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done);
        await AddPaymentAsync(shop, job, 150m, today);
        await AddExpenseAsync(shop, "Utilities", 40.5m, today);

        var ownerHtml = (await TestData.BodyAsync(await factory.CreateClientFor(owner, shop).GetAsync("/"))).Replace("\n", " ");
        var cashierHtml = await TestData.BodyAsync(await factory.CreateClientFor(cashier, shop).GetAsync("/"));

        Assert.Matches(@"data-profit[^>]*>.*?109\.50", ownerHtml);
        Assert.DoesNotContain("data-profit-card", cashierHtml);
        Assert.DoesNotContain("109.50", cashierHtml);
        Assert.DoesNotContain("Profit this month", cashierHtml);
    }

    // ---------------------------------------------------------------- cash close

    [Fact]
    public async Task The_close_adds_up_the_days_payments_by_method_and_expects_cash_minus_cash_expenses()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Close Math");
        var day = new DateOnly(2026, 3, 10);
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done);
        await AddPaymentAsync(shop, job, 100.10m, day, PaymentMethod.Cash);
        await AddPaymentAsync(shop, job, 20.20m, day, PaymentMethod.Cash);
        await AddPaymentAsync(shop, job, 50m, day, PaymentMethod.Card);
        await AddPaymentAsync(shop, job, 30m, day, PaymentMethod.BankTransfer);
        await AddPaymentAsync(shop, job, 10m, day, PaymentMethod.Wallet);
        await AddPaymentAsync(shop, job, 999m, day.AddDays(1), PaymentMethod.Cash); // another day
        await AddExpenseAsync(shop, "Utilities", 40.05m, day, fromCash: true);
        await AddExpenseAsync(shop, "Rent", 500m, day, fromCash: false);

        using var db = factory.CreateContextFor(shop);
        var figures = await CashClosing.FiguresAsync(db, day);
        Assert.Equal(120.30m, figures.Received.Cash);
        Assert.Equal(50m, figures.Received.Card);
        Assert.Equal(30m, figures.Received.Transfer);
        Assert.Equal(10m, figures.Received.Wallet);
        Assert.Equal(210.30m, figures.Received.Total);
        Assert.Equal(540.05m, figures.ExpensesTotal);
        Assert.Equal(40.05m, figures.CashExpenses);
        Assert.Equal(180.25m, figures.Expected(100m)); // 100 + 120.30 - 40.05

        // Counting 175 is 5.25 short; the owner types only the counted cash and the opening float.
        var client = factory.CreateClientFor(owner, shop);
        var response = await TestData.SubmitFormAsync(client, "/Money/CashClose?handler=Close&date=2026-03-10",
            ("Input.OpeningCash", "100"), ("Input.CountedCash", "175"), ("Input.Note", "Short a little"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verify = factory.CreateContextFor(shop);
        var close = await verify.CashCloses.SingleAsync();
        Assert.Equal(day, close.Date);
        Assert.Equal(120.30m, close.CashReceived);
        Assert.Equal(50m, close.CardReceived);
        Assert.Equal(30m, close.TransferReceived);
        Assert.Equal(10m, close.WalletReceived);
        Assert.Equal(540.05m, close.ExpensesTotal);
        Assert.Equal(40.05m, close.CashExpenses);
        Assert.Equal(180.25m, close.ExpectedCash);
        Assert.Equal(175m, close.CountedCash);
        Assert.Equal(-5.25m, close.Difference);
        Assert.Equal("Short a little", close.Note);

        var page = (await TestData.BodyAsync(await client.GetAsync("/Money/CashClose?date=2026-03-10"))).Replace("\n", " ");
        Assert.Matches(@"data-difference[^>]*>.*?5\.25", page);
        Assert.Contains("The drawer is short.", page);
    }

    [Fact]
    public async Task A_saved_close_is_immutable_and_a_correction_is_a_new_close()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Close Immutable");
        var client = factory.CreateClientFor(owner, shop);
        await TestData.SubmitFormAsync(client, "/Money/CashClose?handler=Close&date=2026-03-10", ("Input.OpeningCash", "0"), ("Input.CountedCash", "10"));

        using (var db = factory.CreateContextFor(shop))
        {
            var close = await db.CashCloses.SingleAsync();
            close.CountedCash = 9999;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var db = factory.CreateContextFor(shop))
        {
            db.CashCloses.Remove(await db.CashCloses.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await TestData.SubmitFormAsync(client, "/Money/CashClose?handler=Close&date=2026-03-10", ("Input.OpeningCash", "0"), ("Input.CountedCash", "12"));
        using var verify = factory.CreateContextFor(shop);
        var closes = await verify.CashCloses.OrderBy(c => c.Id).ToListAsync();
        Assert.Equal([10m, 12m], closes.Select(c => c.CountedCash).ToArray());
    }

    [Fact]
    public async Task The_next_day_starts_from_what_was_counted_and_the_server_ignores_forged_figures()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Close Opening");
        var client = factory.CreateClientFor(owner, shop);
        await TestData.SubmitFormAsync(client, "/Money/CashClose?handler=Close&date=2026-03-10",
            ("Input.OpeningCash", "0"), ("Input.CountedCash", "250.50"),
            ("CashReceived", "1000000"), ("Input.ExpectedCash", "1"), ("Input.Difference", "0")); // forged extras are ignored

        using (var db = factory.CreateContextFor(shop))
        {
            var close = await db.CashCloses.SingleAsync();
            Assert.Equal(0m, close.CashReceived); // nothing was taken that day
            Assert.Equal(0m, close.ExpectedCash);
            Assert.Equal(250.50m, close.Difference);
            Assert.Equal(250.50m, await CashClosing.SuggestedOpeningAsync(db, new DateOnly(2026, 3, 11)));
            Assert.Equal(0m, await CashClosing.SuggestedOpeningAsync(db, new DateOnly(2026, 3, 10)));
        }

        var next = await TestData.BodyAsync(await client.GetAsync("/Money/CashClose?date=2026-03-11"));
        Assert.Matches(@"id=""Input_OpeningCash""[^>]*value=""250\.5""", next); // the opening float starts from the last count
    }

    [Fact]
    public async Task The_page_flags_a_close_when_payments_changed_afterwards_and_a_future_date_means_today()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Close Changed");
        var today = ShopClock.Today();
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done);
        var client = factory.CreateClientFor(owner, shop);
        await TestData.SubmitFormAsync(client, $"/Money/CashClose?handler=Close&date={today:yyyy-MM-dd}", ("Input.OpeningCash", "0"), ("Input.CountedCash", "0"));

        Assert.DoesNotContain("data-changed", await TestData.BodyAsync(await client.GetAsync("/Money/CashClose")));

        await AddPaymentAsync(shop, job, 25m, today);
        Assert.Contains("data-changed", await TestData.BodyAsync(await client.GetAsync("/Money/CashClose")));

        var future = await TestData.BodyAsync(await client.GetAsync($"/Money/CashClose?date={today.AddDays(5):yyyy-MM-dd}"));
        Assert.Contains($"data-day>{today:yyyy-MM-dd}<", future);
    }

    [Fact]
    public async Task Another_shop_never_sees_our_cash_closes_or_takes_our_payments_into_its_day()
    {
        var (ownerA, shopA) = await TestData.CreateOwnerAsync(factory, "Close A");
        var (ownerB, shopB) = await TestData.CreateOwnerAsync(factory, "Close B");
        var day = new DateOnly(2026, 3, 10);
        var jobA = await TestData.SeedJobAsync(factory, shopA, JobStatus.Done);
        await AddPaymentAsync(shopA, jobA, 321.99m, day);
        await TestData.SubmitFormAsync(factory.CreateClientFor(ownerA, shopA), "/Money/CashClose?handler=Close&date=2026-03-10",
            ("Input.OpeningCash", "0"), ("Input.CountedCash", "321.99"));

        var clientB = factory.CreateClientFor(ownerB, shopB);
        var page = await TestData.BodyAsync(await clientB.GetAsync("/Money/CashClose?date=2026-03-10"));
        Assert.DoesNotContain("321.99", page);
        Assert.Contains("This day has not been closed yet.", page);

        using var dbB = factory.CreateContextFor(shopB);
        Assert.Empty(await dbB.CashCloses.ToListAsync());
        var figures = await CashClosing.FiguresAsync(dbB, day);
        Assert.Equal(0m, figures.Received.Total);

        // And the context refuses to write a close for another shop.
        dbB.CashCloses.Add(new CashClose { ShopId = shopA, Date = day, ClosedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbB.SaveChangesAsync());
    }

    // ---------------------------------------------------------------- CSV

    [Fact]
    public void CSV_cells_that_could_run_as_a_formula_get_a_leading_quote()
    {
        Assert.Equal("'=1+1", CsvExport.Text("=1+1"));
        Assert.Equal("'+49 170", CsvExport.Text("+49 170"));
        Assert.Equal("'-5", CsvExport.Text("-5"));
        Assert.Equal("'@SUM(A1)", CsvExport.Text("@SUM(A1)"));
        Assert.Equal("'\tcmd", CsvExport.Text("\tcmd"));
        Assert.Equal("\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"", CsvExport.Text("=HYPERLINK(\"http://x\",\"y\")"));
        Assert.Equal("Ahmed", CsvExport.Text("Ahmed"));
        Assert.Equal("", CsvExport.Text(null));
    }

    [Fact]
    public void CSV_quotes_commas_quotes_and_line_breaks_and_writes_numbers_the_same_everywhere()
    {
        Assert.Equal("\"a,b\"", CsvExport.Text("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvExport.Text("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", CsvExport.Text("line1\nline2"));
        Assert.Equal("1234.50", CsvExport.Number(1234.5m));
        Assert.Equal("-5.25", CsvExport.Number(-5.25m)); // a real number, not a text cell: no quote
        Assert.Equal("2026-03-09", CsvExport.Date(new DateOnly(2026, 3, 9)));
    }

    [Fact]
    public void The_file_starts_with_a_utf8_byte_order_mark_and_keeps_arabic_intact()
    {
        var bytes = CsvExport.Build(["التاريخ", "المبلغ"], [["2026-03-01", CsvExport.Text("أحمد, علي")]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal("التاريخ,المبلغ\r\n2026-03-01,\"أحمد, علي\"\r\n", text);
    }

    private async Task<(string Owner, int Shop)> SeedExportShopAsync()
    {
        var (owner, shop) = await TestData.CreateOwnerAsync(factory, "Export " + Guid.NewGuid().ToString("N")[..6]);
        var job = await TestData.SeedJobAsync(factory, shop, JobStatus.Done, customerName: "=cmd|' /C calc'!A0", plate: "EXP 1");
        await AddPaymentAsync(shop, job, 50m, new DateOnly(2026, 3, 5));
        await AddPaymentAsync(shop, job, 20m, new DateOnly(2026, 5, 5)); // outside the range
        await AddExpenseAsync(shop, "Rent", 900m, new DateOnly(2026, 3, 6), note: "+evil, \"quoted\"");
        return (owner, shop);
    }

    [Fact]
    public async Task The_payments_export_is_a_utf8_csv_with_a_bom_only_for_the_range_and_defuses_formulas()
    {
        var (owner, shop) = await SeedExportShopAsync();
        var response = await factory.CreateClientFor(owner, shop).GetAsync("/Money/Export?handler=Download&kind=payments&from=2026-03-01&to=2026-03-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains(".csv", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date,Invoice number,Job card,Customer,Plate number,Payment method,Amount (EGP),Note", lines[0]);
        Assert.Equal(2, lines.Length); // header and the one payment in March
        Assert.Contains("2026-03-05", lines[1]);
        Assert.Contains("50.00", lines[1]);
        Assert.Contains("'=cmd|' /C calc'!A0", lines[1]); // the formula is a quoted text now
        Assert.DoesNotContain(",=cmd", lines[1]);
    }

    [Fact]
    public async Task The_expenses_and_jobs_exports_work_and_never_include_another_shop()
    {
        var (owner, shop) = await SeedExportShopAsync();
        var (_, other) = await TestData.CreateOwnerAsync(factory, "Export Other");
        await AddExpenseAsync(other, "Rent", 12345.67m, new DateOnly(2026, 3, 6), note: "Other shop rent");
        var client = factory.CreateClientFor(owner, shop);

        var expenses = await TestData.BodyAsync(await client.GetAsync("/Money/Export?handler=Download&kind=expenses&from=2026-03-01&to=2026-03-31"));
        Assert.Contains("900.00", expenses);
        Assert.Contains("'+evil", expenses); // the note starts with +
        Assert.Contains("\"\"quoted\"\"", expenses);
        Assert.DoesNotContain("12345.67", expenses);
        Assert.DoesNotContain("Other shop rent", expenses);

        var jobs = await TestData.BodyAsync(await client.GetAsync("/Money/Export?handler=Download&kind=jobs&from=2026-03-01&to=2026-03-31"));
        Assert.Contains("EXP 1", jobs);
        Assert.Contains("203.45", jobs); // 123.45 part + 80 labor
        Assert.Contains("Job card,Invoice number,Visit date,Status,Customer,Plate number,Parts,Labor,Discount,VAT,Total,Received,Balance due,Part cost,Profit", jobs);
    }

    [Fact]
    public async Task Export_refuses_a_bad_kind_a_backwards_range_and_a_range_over_a_year()
    {
        var (owner, shop) = await SeedExportShopAsync();
        var client = factory.CreateClientFor(owner, shop);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Money/Export?handler=Download&kind=users&from=2026-03-01&to=2026-03-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Money/Export?handler=Download&kind=payments&from=2026-03-31&to=2026-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Money/Export?handler=Download&kind=payments&from=2020-01-01&to=2026-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Money/Export?handler=Download&kind=payments&from=garbage&to=2026-03-01")).StatusCode);
    }

    [Fact]
    public async Task The_export_has_arabic_column_titles_when_the_app_is_in_arabic()
    {
        var (owner, shop) = await SeedExportShopAsync();
        var client = factory.CreateClientFor(owner, shop);
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar");

        var csv = await TestData.BodyAsync(await client.GetAsync("/Money/Export?handler=Download&kind=payments&from=2026-03-01&to=2026-03-31"));

        var header = csv.TrimStart('﻿').Split("\r\n")[0];
        Assert.Contains("رقم الفاتورة", header);
        Assert.Contains("المبلغ (جنيه)", header);
        Assert.DoesNotContain("Invoice number", header);

        var page = await TestData.BodyAsync(await client.GetAsync("/Money/Profit"));
        Assert.Contains("dir=\"rtl\"", page);
        Assert.Contains("على أساس النقد", page);
    }
}
