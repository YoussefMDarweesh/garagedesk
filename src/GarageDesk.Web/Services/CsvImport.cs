using System.Text;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

// ----------------------------------------------------------------------------------------------------------------
// CSV import of customers and their cars (growth release). No package: a small RFC 4180 reader, a planner that
// decides what every row would do, and a transactional commit. The planner is pure (it only reads the shop's existing
// phones and plates), so the preview and the commit can never disagree about what a file means.
// ----------------------------------------------------------------------------------------------------------------

/// <summary>Limits that keep an upload small and the preview fast.</summary>
public static class CsvImportLimits
{
    /// <summary>The largest file accepted. A 2,000-row list is about 150 KB.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>Data rows (the header is not counted).</summary>
    public const int MaxRows = 2000;

    public const int MaxColumns = 20;

    /// <summary>No cell of a real customer list is longer than this.</summary>
    public const int MaxCellLength = 1000;

    /// <summary>The preview table shows at most this many rows; the counters and the commit always cover all of them.</summary>
    public const int PreviewRows = 200;
}

/// <summary>Why a whole file was refused (nothing is previewed or imported).</summary>
public enum CsvFileProblem
{
    None,
    NoFile,
    TooLarge,
    Empty,
    NotUtf8,
    Utf16,
    UnclosedQuote,
    TooManyRows,
    TooManyColumns,
    MissingColumns,
}

/// <summary>Reads the bytes of an upload into text: UTF-8 with or without a BOM, nothing else.</summary>
public static class CsvText
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static CsvFileProblem TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        text = "";
        if (bytes.Length == 0)
        {
            return CsvFileProblem.Empty;
        }

        if (bytes.Length > CsvImportLimits.MaxBytes)
        {
            return CsvFileProblem.TooLarge;
        }

        // "Unicode text" saved by Excel is UTF-16. We only read UTF-8, and say so clearly.
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return CsvFileProblem.Utf16;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        try
        {
            text = Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Typically "CSV (Comma delimited)" from an Arabic-language Excel (Windows-1256), not UTF-8.
            return CsvFileProblem.NotUtf8;
        }

        // A NUL byte decodes fine as UTF-8 but no real CSV has one: it is a binary file with a .csv name.
        return text.Contains('\0') ? CsvFileProblem.NotUtf8 : CsvFileProblem.None;
    }
}

/// <summary>A small, strict-enough RFC 4180 reader: quoted cells, doubled quotes, line breaks inside quotes, comma or semicolon.</summary>
public static class CsvReader
{
    /// <summary>Comma, unless the header line (outside quotes) has more semicolons: Excel with a European or Arabic locale writes ";".</summary>
    public static char DetectDelimiter(string text)
    {
        // Excel's optional first line "sep=;" says it outright.
        if (LooksLikeSepHint(text) && text[4] is ',' or ';')
        {
            return text[4];
        }

        int commas = 0, semicolons = 0;
        var inQuotes = false;
        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (ch is '\n' or '\r')
                {
                    if (commas + semicolons > 0)
                    {
                        break; // the header line has been counted
                    }
                }
                else if (ch == ',') commas++;
                else if (ch == ';') semicolons++;
            }
        }

        return semicolons > commas ? ';' : ',';
    }

    private static bool LooksLikeSepHint(string text)
    {
        var end = text.IndexOfAny(['\n', '\r']);
        var first = end < 0 ? text : text[..end];
        return first.Length == 5 && first.StartsWith("sep=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Splits the text into records. Blank records are dropped. Returns a problem (and no records) for an unclosed
    /// quote or when the file has more records or columns than the limits allow.
    /// </summary>
    public static CsvFileProblem TryParse(string text, out List<List<string>> records, out char delimiter)
    {
        records = [];
        var collected = new List<List<string>>();
        delimiter = DetectDelimiter(text);

        // Skip Excel's "sep=;" hint line, if present.
        if (LooksLikeSepHint(text))
        {
            text = text[(text.IndexOfAny(['\n', '\r']) + 1)..];
        }

        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false; // a quote only opens a quoted cell at the very start of a cell

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        CsvFileProblem EndRecord()
        {
            EndField();
            // Drop blank lines (a lone empty cell, or only empty cells like ",,,").
            if (row.Any(c => c.Trim().Length > 0))
            {
                if (row.Count > CsvImportLimits.MaxColumns)
                {
                    return CsvFileProblem.TooManyColumns;
                }

                collected.Add(row);
                // +1 for the header row.
                if (collected.Count > CsvImportLimits.MaxRows + 1)
                {
                    return CsvFileProblem.TooManyRows;
                }
            }
            row = [];
            return CsvFileProblem.None;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (ch == delimiter)
            {
                EndField();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                var problem = EndRecord();
                if (problem != CsvFileProblem.None)
                {
                    records = [];
                    return problem;
                }
            }
            else
            {
                field.Append(ch);
                fieldStarted = true;
            }
        }

        if (inQuotes)
        {
            records = [];
            return CsvFileProblem.UnclosedQuote;
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            var problem = EndRecord();
            if (problem != CsvFileProblem.None)
            {
                records = [];
                return problem;
            }
        }

        records = collected;
        return CsvFileProblem.None;
    }
}

/// <summary>Writing CSV safely: the cell quoting and the guard against spreadsheet formulas.</summary>
public static class CsvWriter
{
    /// <summary>
    /// A cell that starts with = + - @ (or a tab or carriage return) is run as a formula by Excel and Google Sheets
    /// (OWASP "CSV injection"). A leading apostrophe makes the spreadsheet show it as plain text.
    /// </summary>
    public static string NeutralizeFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    public static string Cell(string value)
    {
        var safe = NeutralizeFormula(value);
        return safe.IndexOfAny([',', ';', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"") + "\"" : safe;
    }

    public static string Line(IEnumerable<string> cells) => string.Join(',', cells.Select(Cell));

    /// <summary>UTF-8 with a BOM (so Excel shows Arabic correctly) and CRLF line ends.</summary>
    public static byte[] ToFileBytes(IEnumerable<string> lines) =>
        [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n")];
}

/// <summary>The columns of the import file, recognised from the header in English or Arabic.</summary>
public enum ImportColumn { Name, Phone, Notes, Plate, Make, Model, Year, Mileage, Vin }

public static class ImportTemplate
{
    public static readonly string[] EnglishHeaders =
        ["Customer name", "Phone", "Notes", "Plate", "Make", "Model", "Year", "Mileage", "VIN"];

    public static readonly string[] ArabicHeaders =
        ["اسم العميل", "الهاتف", "ملاحظات", "رقم اللوحة", "الماركة", "الموديل", "السنة", "العداد", "رقم الشاسيه"];

    private static readonly Dictionary<ImportColumn, string[]> Synonyms = new()
    {
        [ImportColumn.Name] = ["customer name", "customername", "customer", "name", "اسم العميل", "العميل", "الاسم", "الإسم"],
        [ImportColumn.Phone] = ["phone", "phone number", "phonenumber", "mobile", "tel", "telephone", "الهاتف", "رقم الهاتف", "الموبايل", "رقم الموبايل", "التليفون", "رقم التليفون"],
        [ImportColumn.Notes] = ["notes", "note", "ملاحظات", "ملاحظة"],
        [ImportColumn.Plate] = ["plate", "plate number", "platenumber", "license plate", "رقم اللوحة", "اللوحة", "رقم السيارة", "اللوحه"],
        [ImportColumn.Make] = ["make", "brand", "الماركة", "الماركه", "الشركة المصنعة"],
        [ImportColumn.Model] = ["model", "الموديل", "الطراز"],
        [ImportColumn.Year] = ["year", "السنة", "السنه", "سنة الصنع"],
        [ImportColumn.Mileage] = ["mileage", "mileage (km)", "odometer", "km", "العداد", "الكيلومترات", "كيلومترات", "المسافة"],
        [ImportColumn.Vin] = ["vin", "chassis", "chassis number", "رقم الشاسيه", "الشاسيه", "رقم الشاسية", "رقم الهيكل"],
    };

    public static byte[] FileBytes(bool arabic) =>
        CsvWriter.ToFileBytes([string.Join(',', arabic ? ArabicHeaders : EnglishHeaders)]);

    /// <summary>
    /// Maps the header cells to columns. Unknown columns are returned in <paramref name="ignored"/> (shown in the
    /// preview, not an error). The first column that matches a name wins.
    /// </summary>
    public static Dictionary<ImportColumn, int> MapHeader(IReadOnlyList<string> header, out List<string> ignored)
    {
        var map = new Dictionary<ImportColumn, int>();
        ignored = [];
        for (var i = 0; i < header.Count; i++)
        {
            var key = NormalizeHeader(header[i]);
            ImportColumn? found = null;
            foreach (var (column, names) in Synonyms)
            {
                if (names.Contains(key))
                {
                    found = column;
                    break;
                }
            }

            if (found is { } c && !map.ContainsKey(c))
            {
                map[c] = i;
            }
            else if (header[i].Trim().Length > 0)
            {
                ignored.Add(header[i].Trim());
            }
        }

        return map;
    }

    private static string NormalizeHeader(string header)
    {
        var text = TextNormalization.ToAsciiDigits(header).Trim().Trim('﻿').ToLowerInvariant();
        text = text.Replace('_', ' ').Replace('-', ' ');
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        return text;
    }

    /// <summary>The columns every file needs: a name and a phone. The car columns are all needed as soon as any one is present.</summary>
    public static List<ImportColumn> MissingColumns(IReadOnlyDictionary<ImportColumn, int> map)
    {
        var missing = new List<ImportColumn>();
        foreach (var required in new[] { ImportColumn.Name, ImportColumn.Phone })
        {
            if (!map.ContainsKey(required))
            {
                missing.Add(required);
            }
        }

        var carColumns = new[] { ImportColumn.Plate, ImportColumn.Make, ImportColumn.Model, ImportColumn.Year };
        if (carColumns.Any(map.ContainsKey))
        {
            missing.AddRange(carColumns.Where(c => !map.ContainsKey(c)));
        }

        return missing;
    }
}

/// <summary>What is wrong with one row. The page turns these into (translated) sentences.</summary>
public enum ImportIssue
{
    NameRequired,
    NameTooLong,
    PhoneRequired,
    PhoneInvalid,
    NotesTooLong,
    PlateRequired,
    PlateInvalid,
    PlateTooLong,
    MakeRequired,
    MakeTooLong,
    ModelRequired,
    ModelTooLong,
    YearInvalid,
    MileageInvalid,
    VinTooLong,
    StartsLikeFormula,
    CellTooLong,
}

/// <summary>What one row will do when the import is committed.</summary>
public enum ImportAction
{
    /// <summary>A new customer and a new car.</summary>
    AddCustomerAndCar,

    /// <summary>A new car for a customer that already exists (in the shop, or earlier in this file).</summary>
    AddCar,

    /// <summary>A new customer with no car.</summary>
    AddCustomer,

    /// <summary>The plate is already a car of this shop: skipped, so uploading the same file twice adds nothing.</summary>
    SkipCarExists,

    /// <summary>The same plate appeared earlier in this file.</summary>
    SkipRepeatedInFile,

    /// <summary>A customer-only row for a phone that is already a customer (or appeared earlier).</summary>
    SkipCustomerExists,

    Error,
}

/// <summary>One row of the preview. <see cref="Cells"/> are the values as read (trimmed), for showing back to the user.</summary>
public sealed class ImportRowPlan
{
    public int Row { get; init; }

    public IReadOnlyDictionary<ImportColumn, string> Cells { get; init; } = new Dictionary<ImportColumn, string>();

    public ImportAction Action { get; set; }

    public List<ImportIssue> Issues { get; } = [];

    /// <summary>Set when the customer already exists in the shop: its current name (the row's name is not applied).</summary>
    public string? ExistingCustomerName { get; set; }

    public string NormalizedPhone { get; set; } = "";

    public string PlateKey { get; set; } = "";

    public int Year { get; set; }

    public int Mileage { get; set; }

    public bool HasCar { get; set; }

    public bool WillImport => Action is ImportAction.AddCustomerAndCar or ImportAction.AddCar or ImportAction.AddCustomer;

    public string Cell(ImportColumn column) => Cells.TryGetValue(column, out var value) ? value : "";
}

public sealed class ImportPlan
{
    public CsvFileProblem Problem { get; init; }

    /// <summary>For <see cref="CsvFileProblem.MissingColumns"/>: which ones.</summary>
    public IReadOnlyList<ImportColumn> MissingColumns { get; init; } = [];

    public IReadOnlyList<string> IgnoredColumns { get; init; } = [];

    public char Delimiter { get; init; } = ',';

    public IReadOnlyList<ImportRowPlan> Rows { get; init; } = [];

    public int CustomersToAdd => Rows.Count(r => r.Action is ImportAction.AddCustomerAndCar or ImportAction.AddCustomer);

    public int CarsToAdd => Rows.Count(r => r.Action is ImportAction.AddCustomerAndCar or ImportAction.AddCar);

    public int Skipped => Rows.Count(r => r.Action is ImportAction.SkipCarExists or ImportAction.SkipRepeatedInFile or ImportAction.SkipCustomerExists);

    public int Errors => Rows.Count(r => r.Action == ImportAction.Error);

    public bool CanCommit => Problem == CsvFileProblem.None && Rows.Any(r => r.WillImport);
}

/// <summary>Turns a file into a plan, and a plan into rows. Reads and writes only through the (tenant-filtered) DbContext.</summary>
public static class CsvImportService
{
    /// <summary>Plans an upload: decode, parse, map the header, validate every row and compare with the shop's data.</summary>
    public static async Task<ImportPlan> PlanAsync(ApplicationDbContext db, byte[]? bytes, CancellationToken ct = default)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return new ImportPlan { Problem = CsvFileProblem.NoFile };
        }

        var decoded = CsvText.TryDecode(bytes, out var text);
        if (decoded != CsvFileProblem.None)
        {
            return new ImportPlan { Problem = decoded };
        }

        var parsed = CsvReader.TryParse(text, out var records, out var delimiter);
        if (parsed != CsvFileProblem.None)
        {
            return new ImportPlan { Problem = parsed, Delimiter = delimiter };
        }

        if (records.Count == 0)
        {
            return new ImportPlan { Problem = CsvFileProblem.Empty, Delimiter = delimiter };
        }

        var map = ImportTemplate.MapHeader(records[0], out var ignored);
        var missing = ImportTemplate.MissingColumns(map);
        if (missing.Count > 0)
        {
            return new ImportPlan { Problem = CsvFileProblem.MissingColumns, MissingColumns = missing, IgnoredColumns = ignored, Delimiter = delimiter };
        }

        // The shop's existing phones and plates. Both queries are limited to the signed-in shop by the query filter.
        var customerByPhone = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await db.Customers.AsNoTracking().Select(c => new { c.Phone, c.Name }).ToListAsync(ct))
        {
            customerByPhone.TryAdd(c.Phone, c.Name);
        }

        var existingPlates = (await db.Vehicles.AsNoTracking().Select(v => v.PlateKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var rows = Plan(records, map, customerByPhone, existingPlates);
        return new ImportPlan { Problem = CsvFileProblem.None, IgnoredColumns = ignored, Delimiter = delimiter, Rows = rows };
    }

    /// <summary>The row-by-row decisions. Pure: no database, so it is easy to test.</summary>
    public static List<ImportRowPlan> Plan(
        List<List<string>> records,
        IReadOnlyDictionary<ImportColumn, int> map,
        IReadOnlyDictionary<string, string> customerByPhone,
        IReadOnlySet<string> existingPlates)
    {
        var rows = new List<ImportRowPlan>();
        var phonesInFile = new HashSet<string>(StringComparer.Ordinal);
        var platesInFile = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            var cells = new Dictionary<ImportColumn, string>();
            foreach (var (column, index) in map)
            {
                // Cut at one past the cell limit: still detected as "too long", but never echoed back in full.
                var value = index < record.Count ? record[index].Trim() : "";
                cells[column] = value.Length > CsvImportLimits.MaxCellLength + 1 ? value[..(CsvImportLimits.MaxCellLength + 1)] : value;
            }

            var row = new ImportRowPlan { Row = i + 1, Cells = cells };
            Validate(row);

            if (row.Issues.Count > 0)
            {
                row.Action = ImportAction.Error;
                rows.Add(row);
                continue;
            }

            var customerExists = customerByPhone.TryGetValue(row.NormalizedPhone, out var existingName);
            var customerSeenInFile = phonesInFile.Contains(row.NormalizedPhone);
            if (customerExists)
            {
                row.ExistingCustomerName = existingName;
            }

            if (!row.HasCar)
            {
                row.Action = customerExists || customerSeenInFile ? ImportAction.SkipCustomerExists : ImportAction.AddCustomer;
                phonesInFile.Add(row.NormalizedPhone);
            }
            else if (existingPlates.Contains(row.PlateKey))
            {
                row.Action = ImportAction.SkipCarExists;
            }
            else if (!platesInFile.Add(row.PlateKey))
            {
                row.Action = ImportAction.SkipRepeatedInFile;
            }
            else
            {
                row.Action = customerExists || customerSeenInFile ? ImportAction.AddCar : ImportAction.AddCustomerAndCar;
                phonesInFile.Add(row.NormalizedPhone);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void Validate(ImportRowPlan row)
    {
        var name = row.Cell(ImportColumn.Name);
        var phone = row.Cell(ImportColumn.Phone);
        var notes = row.Cell(ImportColumn.Notes);
        var plate = row.Cell(ImportColumn.Plate);
        var make = row.Cell(ImportColumn.Make);
        var model = row.Cell(ImportColumn.Model);
        var year = TextNormalization.ToAsciiDigits(row.Cell(ImportColumn.Year)).Replace(" ", "");
        var mileage = TextNormalization.ToAsciiDigits(row.Cell(ImportColumn.Mileage)).Replace(" ", "").Replace(",", "").Replace("٬", "");
        var vin = row.Cell(ImportColumn.Vin);

        // A cell that Excel would run as a formula is refused outright, so it never reaches the database
        // (and so it can never come back out in an export). The phone and the numbers are checked by their own parsers.
        foreach (var text in new[] { name, notes, plate, make, model, vin })
        {
            if (text.Length > CsvImportLimits.MaxCellLength)
            {
                row.Issues.Add(ImportIssue.CellTooLong);
                return;
            }

            if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                row.Issues.Add(ImportIssue.StartsLikeFormula);
                return;
            }
        }

        if (name.Length == 0) row.Issues.Add(ImportIssue.NameRequired);
        else if (name.Length > 100) row.Issues.Add(ImportIssue.NameTooLong);

        if (phone.Length == 0) row.Issues.Add(ImportIssue.PhoneRequired);
        else if (!EgyptianPhone.TryNormalize(phone, out var normalized)) row.Issues.Add(ImportIssue.PhoneInvalid);
        else row.NormalizedPhone = normalized;

        if (notes.Length > 1000) row.Issues.Add(ImportIssue.NotesTooLong);

        // A row describes a car as soon as any car cell is filled in; then the car cells are all checked.
        row.HasCar = plate.Length > 0 || make.Length > 0 || model.Length > 0 || year.Length > 0 || mileage.Length > 0 || vin.Length > 0;
        if (!row.HasCar)
        {
            return;
        }

        if (plate.Length == 0) row.Issues.Add(ImportIssue.PlateRequired);
        else if (plate.Length > 20) row.Issues.Add(ImportIssue.PlateTooLong);
        else
        {
            row.PlateKey = PlateNormalizer.Key(plate);
            if (row.PlateKey.Length == 0) row.Issues.Add(ImportIssue.PlateInvalid);
        }

        if (make.Length == 0) row.Issues.Add(ImportIssue.MakeRequired);
        else if (make.Length > 50) row.Issues.Add(ImportIssue.MakeTooLong);

        if (model.Length == 0) row.Issues.Add(ImportIssue.ModelRequired);
        else if (model.Length > 50) row.Issues.Add(ImportIssue.ModelTooLong);

        if (!int.TryParse(year, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var y) || y is < 1950 or > 2100)
        {
            row.Issues.Add(ImportIssue.YearInvalid);
        }
        else
        {
            row.Year = y;
        }

        if (mileage.Length > 0)
        {
            if (!int.TryParse(mileage, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var km) || km > 5_000_000)
            {
                row.Issues.Add(ImportIssue.MileageInvalid);
            }
            else
            {
                row.Mileage = km;
            }
        }

        if (vin.Length > 17) row.Issues.Add(ImportIssue.VinTooLong);
    }

    /// <summary>The result of a commit.</summary>
    public sealed record CommitResult(int CustomersAdded, int CarsAdded, int Skipped, int Errors);

    /// <summary>
    /// Plans the file again against the data as it is right now and writes every row that will import, in one
    /// transaction: all of them or none. Planning again (instead of trusting the preview) is what makes a second
    /// upload of the same file add nothing, and it means a tampered form can never import anything the rules refuse.
    /// </summary>
    public static async Task<(ImportPlan Plan, CommitResult? Result)> CommitAsync(
        ApplicationDbContext db, int shopId, byte[] bytes, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Two imports for one shop at the same moment run one after the other (a no-op UPDATE locks the shop row in
        // PostgreSQL), so neither can plan against data the other is about to change.
        await db.Shops.IgnoreQueryFilters().Where(s => s.Id == shopId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, x => x.Name), ct);

        var plan = await PlanAsync(db, bytes, ct);
        if (!plan.CanCommit)
        {
            return (plan, null);
        }

        // The customers that already exist, loaded once (tracked, so new cars can point at them).
        var wanted = plan.Rows.Where(r => r.WillImport && r.ExistingCustomerName is not null).Select(r => r.NormalizedPhone).Distinct().ToList();
        var customers = new Dictionary<string, Customer>(StringComparer.Ordinal);
        if (wanted.Count > 0)
        {
            foreach (var existing in await db.Customers.Where(c => wanted.Contains(c.Phone)).ToListAsync(ct))
            {
                customers.TryAdd(existing.Phone, existing);
            }
        }

        foreach (var row in plan.Rows.Where(r => r.WillImport))
        {
            var phone = row.NormalizedPhone;
            if (!customers.TryGetValue(phone, out var customer))
            {
                customer = new Customer
                {
                    Name = row.Cell(ImportColumn.Name),
                    Phone = phone,
                    Notes = row.Cell(ImportColumn.Notes) is { Length: > 0 } n ? n : null,
                };
                db.Customers.Add(customer); // ShopId is filled in by the DbContext
                customers[phone] = customer;
            }

            if (row.HasCar)
            {
                db.Vehicles.Add(new Vehicle
                {
                    Customer = customer,
                    Plate = row.Cell(ImportColumn.Plate),
                    PlateKey = row.PlateKey,
                    Make = row.Cell(ImportColumn.Make),
                    Model = row.Cell(ImportColumn.Model),
                    Year = row.Year,
                    Mileage = row.Mileage,
                    Vin = row.Cell(ImportColumn.Vin) is { Length: > 0 } v ? v.ToUpperInvariant() : null,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (plan, new CommitResult(plan.CustomersToAdd, plan.CarsToAdd, plan.Skipped, plan.Errors));
    }

    /// <summary>A CSV of the rows that were not importable (errors and skipped duplicates), for fixing the file.</summary>
    public static byte[] ReportBytes(ImportPlan plan, Func<ImportRowPlan, string> describe)
    {
        var lines = new List<string> { CsvWriter.Line(["Row", "Result", .. ImportTemplate.EnglishHeaders]) };
        foreach (var row in plan.Rows.Where(r => !r.WillImport))
        {
            lines.Add(CsvWriter.Line([
                row.Row.ToString(System.Globalization.CultureInfo.InvariantCulture),
                describe(row),
                row.Cell(ImportColumn.Name), row.Cell(ImportColumn.Phone), row.Cell(ImportColumn.Notes),
                row.Cell(ImportColumn.Plate), row.Cell(ImportColumn.Make), row.Cell(ImportColumn.Model),
                row.Cell(ImportColumn.Year), row.Cell(ImportColumn.Mileage), row.Cell(ImportColumn.Vin),
            ]));
        }

        return CsvWriter.ToFileBytes(lines);
    }
}
