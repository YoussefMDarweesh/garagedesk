using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>Growth: the CSV reader and planner (pure), then the import page end to end.</summary>
public partial class CsvImportTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private const string Header = "Customer name,Phone,Notes,Plate,Make,Model,Year,Mileage,VIN";

    private static byte[] Bytes(string text, bool bom = false) =>
        [.. (bom ? Encoding.UTF8.GetPreamble() : []), .. new UTF8Encoding(false).GetBytes(text)];

    private static List<List<string>> Parse(string text, out char delimiter)
    {
        Assert.Equal(CsvFileProblem.None, CsvReader.TryParse(text, out var records, out delimiter));
        return records;
    }

    // ---- the reader ----

    [Fact]
    public void The_reader_handles_quotes_doubled_quotes_and_line_breaks_inside_a_cell()
    {
        var records = Parse("a,b,c\r\n\"x, y\",\"he said \"\"hi\"\"\",\"line1\nline2\"\r\n", out var delimiter);

        Assert.Equal(',', delimiter);
        Assert.Equal(2, records.Count);
        Assert.Equal(["x, y", "he said \"hi\"", "line1\nline2"], records[1]);
    }

    [Fact]
    public void Semicolon_files_from_excel_are_detected_and_commas_inside_cells_survive()
    {
        var records = Parse("Name;Phone;Notes\n\"Ali, Jr\";01012345678;ok\n", out var delimiter);

        Assert.Equal(';', delimiter);
        Assert.Equal(["Ali, Jr", "01012345678", "ok"], records[1]);
    }

    [Fact]
    public void An_excel_sep_hint_line_is_skipped_and_obeyed()
    {
        var records = Parse("sep=;\r\nName;Phone\r\nAli;0101\r\n", out var delimiter);

        Assert.Equal(';', delimiter);
        Assert.Equal(["Name", "Phone"], records[0]);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Blank_lines_and_rows_of_only_commas_are_dropped()
    {
        var records = Parse("a,b\n\n,,\n1,2\n\n", out _);

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void An_unclosed_quote_is_a_file_problem_not_a_crash()
    {
        Assert.Equal(CsvFileProblem.UnclosedQuote, CsvReader.TryParse("a,b\n\"oops,1\n", out var records, out _));
        Assert.Empty(records);
    }

    [Fact]
    public void Too_many_rows_or_columns_are_refused()
    {
        var rows = string.Join("\n", Enumerable.Repeat("a,b", CsvImportLimits.MaxRows + 2));
        Assert.Equal(CsvFileProblem.TooManyRows, CsvReader.TryParse(rows, out _, out _));

        var wide = string.Join(",", Enumerable.Repeat("c", CsvImportLimits.MaxColumns + 1));
        Assert.Equal(CsvFileProblem.TooManyColumns, CsvReader.TryParse(wide, out _, out _));

        var exactly = string.Join("\n", Enumerable.Repeat("a,b", CsvImportLimits.MaxRows + 1));
        Assert.Equal(CsvFileProblem.None, CsvReader.TryParse(exactly, out _, out _));
    }

    // ---- bytes to text ----

    [Fact]
    public void Utf8_with_and_without_a_bom_both_decode_and_arabic_survives()
    {
        const string text = "اسم العميل,الهاتف\nمحمود,01012345678";

        Assert.Equal(CsvFileProblem.None, CsvText.TryDecode(Bytes(text), out var plain));
        Assert.Equal(CsvFileProblem.None, CsvText.TryDecode(Bytes(text, bom: true), out var withBom));

        Assert.Equal(text, plain);
        Assert.Equal(text, withBom);
    }

    [Fact]
    public void Utf16_windows_1256_and_binary_files_are_refused_with_the_right_reason()
    {
        Assert.Equal(CsvFileProblem.Utf16, CsvText.TryDecode([0xFF, 0xFE, 0x41, 0x00], out _));
        Assert.Equal(CsvFileProblem.NotUtf8, CsvText.TryDecode([0xC7, 0xE1, 0xC7, 0xD3, 0xE3], out _)); // Arabic in Windows-1256
        Assert.Equal(CsvFileProblem.NotUtf8, CsvText.TryDecode([0x41, 0x00, 0x42], out _)); // NUL byte
        Assert.Equal(CsvFileProblem.Empty, CsvText.TryDecode([], out _));
        Assert.Equal(CsvFileProblem.TooLarge, CsvText.TryDecode(new byte[CsvImportLimits.MaxBytes + 1], out _));
    }

    // ---- header ----

    [Fact]
    public void English_and_arabic_headers_map_to_the_same_columns()
    {
        var en = ImportTemplate.MapHeader(ImportTemplate.EnglishHeaders, out var ignoredEn);
        var ar = ImportTemplate.MapHeader(ImportTemplate.ArabicHeaders, out var ignoredAr);

        Assert.Equal(9, en.Count);
        Assert.Equal(en, ar);
        Assert.Empty(ignoredEn);
        Assert.Empty(ignoredAr);
        Assert.Empty(ImportTemplate.MissingColumns(en));
    }

    [Fact]
    public void Missing_required_columns_and_partial_car_columns_are_reported_and_unknown_ones_ignored()
    {
        var map = ImportTemplate.MapHeader(["Name", "Plate", "Favourite colour"], out var ignored);

        Assert.Equal(["Favourite colour"], ignored);
        var missing = ImportTemplate.MissingColumns(map);
        Assert.Contains(ImportColumn.Phone, missing);
        Assert.Contains(ImportColumn.Make, missing);
        Assert.Contains(ImportColumn.Model, missing);
        Assert.Contains(ImportColumn.Year, missing);
        Assert.DoesNotContain(ImportColumn.Name, missing);
    }

    // ---- the planner ----

    private static List<ImportRowPlan> PlanRows(string csv, Dictionary<string, string>? customers = null, params string[] plates)
    {
        var records = Parse(csv, out _);
        var map = ImportTemplate.MapHeader(records[0], out _);
        return CsvImportService.Plan(records, map, customers ?? new Dictionary<string, string>(), plates.ToHashSet());
    }

    [Fact]
    public void Rows_are_classified_as_new_existing_duplicate_and_error()
    {
        var csv = string.Join("\n",
            Header,
            "Mona,01012345678,,ABC 123,Skoda,Octavia,2019,45000,",       // 2: new customer + car
            "Mona,01012345678,,XYZ 999,Kia,Rio,2020,,",                  // 3: same person (earlier in file) + another car
            "Karim,01100000000,,OLD 1,Fiat,Tipo,2018,,",                 // 4: plate already in shop
            "Sara,01200000000,,,,,,,",                                   // 5: customer only
            "Sara,01200000000,,,,,,,",                                   // 6: customer only again
            "Hany,01500000000,,ABC-123,Honda,Civic,2017,,",              // 7: same plate as row 2, typed differently
            ",01012345678,,,,,,,",                                       // 8: no name
            "Bad Phone,12345,,,,,,,",                                    // 9
            "No Make,01012223333,,PLT 1,,Model,2019,,",                  // 10
            "Old Friend,01099999999,,NEW 5,VW,Golf,2021,,");             // 11: customer exists in the shop

        var rows = PlanRows(csv, new() { ["+201099999999"] = "Old Friend" }, "OLD1");

        Assert.Equal(ImportAction.AddCustomerAndCar, rows[0].Action);
        Assert.Equal(ImportAction.AddCar, rows[1].Action);
        Assert.Equal(ImportAction.SkipCarExists, rows[2].Action);
        Assert.Equal(ImportAction.AddCustomer, rows[3].Action);
        Assert.Equal(ImportAction.SkipCustomerExists, rows[4].Action);
        Assert.Equal(ImportAction.SkipRepeatedInFile, rows[5].Action);
        Assert.Equal(ImportAction.Error, rows[6].Action);
        Assert.Contains(ImportIssue.NameRequired, rows[6].Issues);
        Assert.Contains(ImportIssue.PhoneInvalid, rows[7].Issues);
        Assert.Contains(ImportIssue.MakeRequired, rows[8].Issues);
        Assert.Equal(ImportAction.AddCar, rows[9].Action);
        Assert.Equal("Old Friend", rows[9].ExistingCustomerName);
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10, 11], rows.Select(r => r.Row));
    }

    [Fact]
    public void Phones_and_plates_are_normalized_before_they_are_compared()
    {
        // Arabic-Indic digits in the phone, an Arabic plate with an alef variant, spaces and dashes.
        var csv = Header + "\nمحمود,٠١٠١٢٣٤٥٦٧٨,,أ ب ج ١٢٣٤,Toyota,Corolla,٢٠٢٠,١٢٬٠٠٠,";
        var rows = PlanRows(csv, new() { ["+201012345678"] = "Mahmoud" }, "ابج1234");

        Assert.Equal("+201012345678", rows[0].NormalizedPhone);
        Assert.Equal("ابج1234", rows[0].PlateKey);
        Assert.Equal(2020, rows[0].Year);
        Assert.Equal(12000, rows[0].Mileage);
        Assert.Equal(ImportAction.SkipCarExists, rows[0].Action);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\",\"x\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void A_cell_that_starts_like_a_formula_is_an_error_in_every_text_column(string cell)
    {
        var quoted = "\"" + cell.Replace("\"", "\"\"") + "\"";
        foreach (var csv in new[]
                 {
                     Header + $"\n{quoted},01012345678,,PLT 1,Make,Model,2020,,",
                     Header + $"\nName,01012345678,{quoted},PLT 1,Make,Model,2020,,",
                     Header + $"\nName,01012345678,,{quoted},Make,Model,2020,,",
                     Header + $"\nName,01012345678,,PLT 1,{quoted},Model,2020,,",
                     Header + $"\nName,01012345678,,PLT 1,Make,{quoted},2020,,",
                     Header + $"\nName,01012345678,,PLT 1,Make,Model,2020,,{quoted}",
                 })
        {
            var row = Assert.Single(PlanRows(csv));
            Assert.Equal(ImportAction.Error, row.Action);
            Assert.Contains(ImportIssue.StartsLikeFormula, row.Issues);
        }
    }

    [Fact]
    public void A_plus_sign_is_fine_in_a_phone_number()
    {
        var row = Assert.Single(PlanRows(Header + "\nAli,+20 101 234 5678,,,,,,,"));

        Assert.Equal(ImportAction.AddCustomer, row.Action);
        Assert.Equal("+201012345678", row.NormalizedPhone);
    }

    [Fact]
    public void Limits_on_year_mileage_vin_and_length_are_enforced_like_the_forms()
    {
        var rows = PlanRows(string.Join("\n",
            Header,
            "A,01012345678,,P 1,M,M,1949,,",
            "B,01012345678,,P 2,M,M,2020,5000001,",
            "C,01012345678,,P 3,M,M,2020,12.5,",
            "D,01012345678,,P 4,M,M,2020,,12345678901234567890",
            "E,01012345678,,P 5,M,M,abc,,",
            new string('x', 101) + ",01012345678,,,,,,,"));

        Assert.Contains(ImportIssue.YearInvalid, rows[0].Issues);
        Assert.Contains(ImportIssue.MileageInvalid, rows[1].Issues);
        Assert.Contains(ImportIssue.MileageInvalid, rows[2].Issues);
        Assert.Contains(ImportIssue.VinTooLong, rows[3].Issues);
        Assert.Contains(ImportIssue.YearInvalid, rows[4].Issues);
        Assert.Contains(ImportIssue.NameTooLong, rows[5].Issues);
    }

    [Fact]
    public void A_huge_cell_is_reported_and_never_echoed_in_full()
    {
        var row = Assert.Single(PlanRows(Header + "\n" + new string('N', 50_000) + ",01012345678,,,,,,,"));

        Assert.Equal(ImportAction.Error, row.Action);
        Assert.Contains(ImportIssue.CellTooLong, row.Issues);
        Assert.True(row.Cell(ImportColumn.Name).Length <= CsvImportLimits.MaxCellLength + 1);
    }

    // ---- writing ----

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+20", "'+20")]
    [InlineData("-x", "'-x")]
    [InlineData("@a", "'@a")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void The_writer_quotes_cells_and_neutralizes_formulas(string value, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(value));
    }

    [Fact]
    public void The_template_is_utf8_with_a_bom_and_has_only_the_header()
    {
        var english = ImportTemplate.FileBytes(arabic: false);
        var arabic = ImportTemplate.FileBytes(arabic: true);

        Assert.Equal([0xEF, 0xBB, 0xBF], english[..3]);
        Assert.Equal(Header + "\r\n", Encoding.UTF8.GetString(english, 3, english.Length - 3));
        Assert.StartsWith("اسم العميل,", Encoding.UTF8.GetString(arabic, 3, arabic.Length - 3));
    }

    // ---- the page: who may use it ----

    [Theory]
    [InlineData("/Import", 200, 403, 403)]
    [InlineData("/Import?handler=Template&lang=en", 200, 403, 403)]
    public async Task Only_owners_may_import_and_forbidden_is_a_403(string path, int owner, int cashier, int technician)
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Import Roles");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var technicianId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);

        Assert.Equal((HttpStatusCode)owner, (await factory.CreateClientFor(ownerId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)cashier, (await factory.CreateClientFor(cashierId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)technician, (await factory.CreateClientFor(technicianId, shop).GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_cashier_cannot_post_an_import_even_with_a_valid_form()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "No Cashier Import");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var client = factory.CreateClientFor(cashierId, shop);

        var response = await client.PostAsync("/Import?handler=Commit",
            new FormUrlEncodedContent([new("payload", Convert.ToBase64String(Bytes(Header + "\nA,01012345678,,,,,,,")))]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_open_the_import()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Import")).StatusCode);
    }

    // ---- the page: template, preview, commit ----

    [Theory]
    [InlineData("en", Header)]
    [InlineData("ar", "اسم العميل,الهاتف,ملاحظات,رقم اللوحة,الماركة,الموديل,السنة,العداد,رقم الشاسيه")]
    public async Task The_template_downloads_with_a_bom_and_the_right_headers(string lang, string expectedHeader)
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Template Garage");
        var client = factory.CreateClientFor(userId, shop);

        var response = await client.GetAsync($"/Import?handler=Template&lang={lang}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.StartsWith(expectedHeader, Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public async Task The_preview_shows_every_row_result_and_writes_nothing()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Preview Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = string.Join("\r\n", Header,
            "Mona,01012345678,,ABC 123,Skoda,Octavia,2019,45000,",
            "Bad,555,,,,,,,",
            "Sara,01200000000,,,,,,,");

        var response = await PreviewAsync(client, csv);
        var html = await TestData.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-import-preview", html);
        Assert.Equal("2", Count(html, "customers"));
        Assert.Equal("1", Count(html, "cars"));
        Assert.Equal("1", Count(html, "errors"));
        Assert.Contains("data-action=\"Error\"", html);
        Assert.Contains("Enter a valid Egyptian phone number", html);
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task Values_are_echoed_encoded_so_markup_in_a_file_cannot_run()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Echo Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = Header + "\n\"<script>alert(1)</script>\",01012345678,,,,,,,";

        var raw = await (await PreviewAsync(client, csv)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.Contains("&lt;script&gt;", raw);
    }

    [Fact]
    public async Task Commit_writes_the_good_rows_to_this_shop_only_and_skips_the_bad_ones()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Commit Garage");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Bystander Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = string.Join("\n", Header,
            "Mona,0101 234 5678,\"Prefers WhatsApp, evenings\",ABC 123,skoda,Octavia,2019,45000,tmbab12345",
            "Mona,01012345678,,XYZ 999,Kia,Rio,2020,,",
            "Sara,01200000000,,,,,,,",
            "Broken,nope,,,,,,,");

        var preview = await PreviewAsync(client, csv);
        var commit = await CommitFromAsync(client, preview);

        Assert.Equal(HttpStatusCode.Redirect, commit.StatusCode);
        using var db = factory.CreateContextFor(shop);
        var customers = await db.Customers.Include(c => c.Vehicles).OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(["Mona", "Sara"], customers.Select(c => c.Name));
        Assert.Equal("Prefers WhatsApp, evenings", customers[0].Notes);
        Assert.Equal(2, customers[0].Vehicles.Count);
        Assert.Empty(customers[1].Vehicles);
        var skoda = customers[0].Vehicles.Single(v => v.PlateKey == "ABC123");
        Assert.Equal("TMBAB12345", skoda.Vin);
        Assert.Equal(45000, skoda.Mileage);
        Assert.All(customers, c => Assert.Equal(shop, c.ShopId));

        using var other = factory.CreateContextFor(otherShop);
        Assert.Empty(await other.Customers.ToListAsync());
        Assert.Empty(await other.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task Uploading_the_same_file_again_adds_nothing()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Twice Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = string.Join("\n", Header,
            "Mona,01012345678,,ABC 123,Skoda,Octavia,2019,,",
            "Sara,01200000000,,,,,,,");

        await CommitFromAsync(client, await PreviewAsync(client, csv));

        // The second time the preview shows only skips and offers no import button...
        var again = await PreviewAsync(client, csv);
        var html = await TestData.BodyAsync(again);
        Assert.DoesNotContain("data-import-commit", html);
        Assert.Equal("0", Count(html, "customers"));
        Assert.Equal("2", Count(html, "skipped"));

        // ...and even a commit forced with the same payload changes nothing.
        var forced = await CommitFromAsync(client, again);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.Contains("Nothing to import", await TestData.BodyAsync(forced));

        using var db = factory.CreateContextFor(shop);
        Assert.Equal(2, await db.Customers.CountAsync());
        Assert.Equal(1, await db.Vehicles.CountAsync());
    }

    [Fact]
    public async Task The_same_plate_in_two_shops_is_not_a_duplicate()
    {
        var (_, other) = await TestData.CreateOwnerAsync(factory, "Plate Owner Garage");
        await TestData.SeedJobAsync(factory, other, plate: "SHARED 1", phone: "+201012345678");
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Plate Newcomer Garage");
        var client = factory.CreateClientFor(userId, shop);

        var html = await TestData.BodyAsync(await PreviewAsync(client,
            Header + "\nMona,01012345678,,SHARED 1,Skoda,Octavia,2019,,"));

        Assert.Equal("1", Count(html, "customers"));
        Assert.Equal("1", Count(html, "cars"));
        Assert.Equal("0", Count(html, "skipped"));
    }

    [Fact]
    public async Task An_arabic_semicolon_file_with_a_bom_imports_correctly()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Arabic Import Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = string.Join("\r\n",
            "اسم العميل;الهاتف;ملاحظات;رقم اللوحة;الماركة;الموديل;السنة;العداد;رقم الشاسيه",
            "محمود حسن;٠١٠٢٣٤٥٦٧٨٩;عميل مميز;س ص ع ١٢٣;تويوتا;كورولا;٢٠٢٠;٣٠٠٠٠;");

        await CommitFromAsync(client, await PreviewAsync(client, csv, bom: true));

        using var db = factory.CreateContextFor(shop);
        var customer = await db.Customers.Include(c => c.Vehicles).SingleAsync();
        Assert.Equal("محمود حسن", customer.Name);
        Assert.Equal("+201023456789", customer.Phone);
        var vehicle = Assert.Single(customer.Vehicles);
        Assert.Equal("تويوتا", vehicle.Make);
        Assert.Equal("سصع123", vehicle.PlateKey);
        Assert.Equal(30000, vehicle.Mileage);
    }

    [Fact]
    public async Task Whole_file_problems_are_explained_and_nothing_is_written()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Problem Garage");
        var client = factory.CreateClientFor(userId, shop);

        var windows1256 = await PostFileAsync(client, [0xC7, 0xE1, 0xC7, 0xD3, 0xE3, 0x2C, 0x0A]);
        Assert.Contains("not saved as UTF-8", await TestData.BodyAsync(windows1256));

        var noPhone = await PreviewAsync(client, "Name,Plate\nAli,ABC 1");
        Assert.Contains("columns are missing", await TestData.BodyAsync(noPhone));

        var empty = await PostFileAsync(client, []);
        Assert.Contains("Choose a CSV file", await TestData.BodyAsync(empty));

        var tooBig = await PostFileAsync(client, new byte[CsvImportLimits.MaxBytes + 10]);
        Assert.Contains("too large", await TestData.BodyAsync(tooBig));

        var tooMany = await PreviewAsync(client, Header + string.Concat(Enumerable.Repeat("\nA,01012345678,,,,,,,", CsvImportLimits.MaxRows + 1)));
        Assert.Contains("more than 2000 rows", await TestData.BodyAsync(tooMany));

        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task A_tampered_or_garbage_payload_is_refused_without_an_error_page()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Tamper Garage");
        var client = factory.CreateClientFor(userId, shop);
        var token = await TestData.TokenAsync(client, "/Import");

        foreach (var payload in new[] { "%%%not base64%%%", "", Convert.ToBase64String(new byte[10]), new string('A', 2_000_000) })
        {
            var response = await client.PostAsync("/Import?handler=Commit", new FormUrlEncodedContent(
                [new("payload", payload), new("__RequestVerificationToken", token)]));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task The_report_lists_the_rows_not_imported_and_neutralizes_formulas()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Report Garage");
        var client = factory.CreateClientFor(userId, shop);
        var csv = string.Join("\n", Header,
            "Fine,01012345678,,ABC 1,Skoda,Octavia,2019,,",
            "\"=HYPERLINK(\"\"http://evil\"\",\"\"x\"\")\",01012345679,,,,,,,");
        var preview = await PreviewAsync(client, csv);

        var report = await ReportFromAsync(client, preview);

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var bytes = await report.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Row,Result,", text);
        Assert.DoesNotContain("Fine", text); // importable rows are not in the report
        Assert.Contains("'=HYPERLINK", text); // the cell is text, not a formula, when opened in Excel
        Assert.DoesNotContain(",=HYPERLINK", text);
        Assert.DoesNotContain("\"=HYPERLINK", text);
    }

    [Fact]
    public async Task Customers_page_offers_the_import_to_owners_only()
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Link Garage");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);

        var owner = await TestData.BodyAsync(await factory.CreateClientFor(ownerId, shop).GetAsync("/Customers"));
        var cashier = await TestData.BodyAsync(await factory.CreateClientFor(cashierId, shop).GetAsync("/Customers"));

        Assert.Contains("href=\"/Import\"", owner);
        Assert.DoesNotContain("href=\"/Import\"", cashier);
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> PreviewAsync(HttpClient client, string csv, bool bom = false) =>
        await PostFileAsync(client, Bytes(csv, bom));

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, byte[] bytes)
    {
        var token = await TestData.TokenAsync(client, "/Import");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("text/csv");
        var form = new MultipartFormDataContent
        {
            { file, "file", "customers.csv" },
            { new StringContent(token), "__RequestVerificationToken" },
        };
        return await client.PostAsync("/Import?handler=Preview", form);
    }

    private static async Task<HttpResponseMessage> CommitFromAsync(HttpClient client, HttpResponseMessage preview) =>
        await PostPayloadAsync(client, preview, "Commit");

    private static async Task<HttpResponseMessage> ReportFromAsync(HttpClient client, HttpResponseMessage preview) =>
        await PostPayloadAsync(client, preview, "Report");

    private static async Task<HttpResponseMessage> PostPayloadAsync(HttpClient client, HttpResponseMessage preview, string handler)
    {
        var html = await TestData.BodyAsync(preview);
        var payload = PayloadField().Match(html);
        Assert.True(payload.Success, "The preview has no payload field");
        var token = TokenField().Match(html);
        Assert.True(token.Success, "The preview has no anti-forgery token");
        return await client.PostAsync($"/Import?handler={handler}", new FormUrlEncodedContent(
        [
            new("payload", payload.Groups[1].Value),
            new("__RequestVerificationToken", token.Groups[1].Value),
        ]));
    }

    private static string Count(string html, string name) =>
        Regex.Match(html, $"data-count=\"{name}\">(\\d+)<").Groups[1].Value;

    [GeneratedRegex("name=\"payload\" value=\"([^\"]*)\"")]
    private static partial Regex PayloadField();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenField();
}
