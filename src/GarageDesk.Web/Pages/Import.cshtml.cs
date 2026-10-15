using System.Globalization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages;

/// <summary>
/// Import customers and their cars from a CSV file: download a template, upload, look at the preview (every row's
/// result, duplicates, errors), then commit in one transaction. Owner only: it adds many records at once.
/// </summary>
[Authorize(Policy = Policies.Owner)]
public class ImportModel(
    ApplicationDbContext db,
    ICurrentShop currentShop,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Set after an upload: what the file would do.</summary>
    public ImportPlan? Plan { get; private set; }

    /// <summary>The uploaded file (base64) carried to the commit button. It is planned again from scratch on commit.</summary>
    public string? Payload { get; private set; }

    /// <summary>A reason the whole file was refused (already translated), or null.</summary>
    public string? FileError { get; private set; }

    public void OnGet()
    {
    }

    /// <summary>The empty template, with English or Arabic headers. Both are understood by the importer.</summary>
    public IActionResult OnGetTemplate(string? lang)
    {
        var arabic = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
        Response.Headers.CacheControl = "no-store";
        return File(ImportTemplate.FileBytes(arabic), "text/csv; charset=utf-8",
            arabic ? "garagedesk-import-template-ar.csv" : "garagedesk-import-template-en.csv");
    }

    /// <summary>Step 1: read the upload and show what it would do. Nothing is written.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            FileError = FileProblemText(CsvFileProblem.NoFile, []);
            return Page();
        }

        if (file.Length > CsvImportLimits.MaxBytes)
        {
            FileError = FileProblemText(CsvFileProblem.TooLarge, []);
            return Page();
        }

        var bytes = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(bytes);
        }

        return await ShowPlanAsync(bytes);
    }

    /// <summary>Step 2: import every row that is good, all or nothing, then go back with a summary.</summary>
    public async Task<IActionResult> OnPostCommitAsync(string? payload)
    {
        var bytes = DecodePayload(payload);
        if (bytes is null)
        {
            FileError = FileProblemText(CsvFileProblem.NoFile, []);
            return Page();
        }

        var (plan, result) = await CsvImportService.CommitAsync(db, currentShop.ShopId!.Value, bytes);
        if (result is null)
        {
            // Nothing to write: the file has a problem, or every row is a duplicate or an error (e.g. it was already imported).
            Plan = plan;
            Payload = payload;
            FileError = plan.Problem != CsvFileProblem.None
                ? FileProblemText(plan.Problem, plan.MissingColumns)
                : localizer["Nothing to import: every row is already in your shop or has an error."].Value;
            return Page();
        }

        StatusMessage = string.Format(CultureInfo.CurrentCulture,
            localizer["Imported {0} customers and {1} cars. {2} rows were skipped as duplicates and {3} had errors."].Value,
            result.CustomersAdded, result.CarsAdded, result.Skipped, result.Errors);
        return RedirectToPage();
    }

    /// <summary>The rows that will not be imported, as a CSV the owner can fix and upload again.</summary>
    public async Task<IActionResult> OnPostReportAsync(string? payload)
    {
        var bytes = DecodePayload(payload);
        if (bytes is null)
        {
            FileError = FileProblemText(CsvFileProblem.NoFile, []);
            return Page();
        }

        var plan = await CsvImportService.PlanAsync(db, bytes);
        if (plan.Problem != CsvFileProblem.None)
        {
            FileError = FileProblemText(plan.Problem, plan.MissingColumns);
            return Page();
        }

        Response.Headers.CacheControl = "no-store";
        return File(CsvImportService.ReportBytes(plan, Describe), "text/csv; charset=utf-8", "garagedesk-import-report.csv");
    }

    private async Task<IActionResult> ShowPlanAsync(byte[] bytes)
    {
        Plan = await CsvImportService.PlanAsync(db, bytes);
        if (Plan.Problem != CsvFileProblem.None)
        {
            FileError = FileProblemText(Plan.Problem, Plan.MissingColumns);
            Plan = null;
            return Page();
        }

        Payload = Convert.ToBase64String(bytes);
        return Page();
    }

    private static byte[]? DecodePayload(string? payload)
    {
        // Base64 is 4/3 of the file; anything much longer than the limit allows is refused before decoding.
        if (string.IsNullOrEmpty(payload) || payload.Length > CsvImportLimits.MaxBytes / 3 * 4 + 16)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(payload);
            return bytes.Length is > 0 and <= CsvImportLimits.MaxBytes ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ---- the words (translated here so the completeness test sees every string) ----

    public string FileProblemText(CsvFileProblem problem, IReadOnlyList<ImportColumn> missing) => problem switch
    {
        CsvFileProblem.NoFile => localizer["Choose a CSV file to upload."].Value,
        CsvFileProblem.TooLarge => localizer["The file is too large. The maximum size is 512 KB."].Value,
        CsvFileProblem.Empty => localizer["The file is empty."].Value,
        CsvFileProblem.NotUtf8 => localizer["The file is not saved as UTF-8. In Excel choose Save As, then CSV UTF-8 (Comma delimited)."].Value,
        CsvFileProblem.Utf16 => localizer["The file is saved as Unicode text (UTF-16). In Excel choose Save As, then CSV UTF-8 (Comma delimited)."].Value,
        CsvFileProblem.UnclosedQuote => localizer["A quotation mark is never closed, so the file cannot be read. Check the cells that contain quotes."].Value,
        CsvFileProblem.TooManyRows => localizer["The file has more than 2000 rows. Split it into smaller files."].Value,
        CsvFileProblem.TooManyColumns => localizer["The file has more than 20 columns."].Value,
        CsvFileProblem.MissingColumns => localizer["These columns are missing from the first row: {0}", string.Join(", ", missing.Select(ColumnName))].Value,
        _ => "",
    };

    public string ColumnName(ImportColumn column) => column switch
    {
        ImportColumn.Name => localizer["Customer name"].Value,
        ImportColumn.Phone => localizer["Phone"].Value,
        ImportColumn.Notes => localizer["Notes"].Value,
        ImportColumn.Plate => localizer["Plate number"].Value,
        ImportColumn.Make => localizer["Make"].Value,
        ImportColumn.Model => localizer["Model"].Value,
        ImportColumn.Year => localizer["Year"].Value,
        ImportColumn.Mileage => localizer["Mileage (km)"].Value,
        _ => localizer["VIN (optional)"].Value,
    };

    public string IssueText(ImportIssue issue) => issue switch
    {
        ImportIssue.NameRequired => localizer["The name is missing."].Value,
        ImportIssue.NameTooLong => localizer["The name is longer than 100 characters."].Value,
        ImportIssue.PhoneRequired => localizer["The phone number is missing."].Value,
        ImportIssue.PhoneInvalid => localizer["Enter a valid Egyptian phone number, for example 0101 234 5678."].Value,
        ImportIssue.NotesTooLong => localizer["The notes are longer than 1000 characters."].Value,
        ImportIssue.PlateRequired => localizer["The plate number is missing."].Value,
        ImportIssue.PlateInvalid => localizer["Enter a valid plate number."].Value,
        ImportIssue.PlateTooLong => localizer["The plate number is longer than 20 characters."].Value,
        ImportIssue.MakeRequired => localizer["The make is missing."].Value,
        ImportIssue.MakeTooLong => localizer["The make is longer than 50 characters."].Value,
        ImportIssue.ModelRequired => localizer["The model is missing."].Value,
        ImportIssue.ModelTooLong => localizer["The model is longer than 50 characters."].Value,
        ImportIssue.YearInvalid => localizer["The year must be between 1950 and 2100."].Value,
        ImportIssue.MileageInvalid => localizer["The mileage must be a whole number of kilometres up to 5,000,000."].Value,
        ImportIssue.VinTooLong => localizer["The VIN is longer than 17 characters."].Value,
        ImportIssue.StartsLikeFormula => localizer["A cell may not start with = + - or @ (a spreadsheet would run it as a formula)."].Value,
        _ => localizer["A cell is longer than 1000 characters."].Value,
    };

    public string ActionText(ImportAction action) => action switch
    {
        ImportAction.AddCustomerAndCar => localizer["New customer and car"].Value,
        ImportAction.AddCar => localizer["New car for an existing customer"].Value,
        ImportAction.AddCustomer => localizer["New customer"].Value,
        ImportAction.SkipCarExists => localizer["Skipped: this plate is already in your shop"].Value,
        ImportAction.SkipRepeatedInFile => localizer["Skipped: this plate appears earlier in the file"].Value,
        ImportAction.SkipCustomerExists => localizer["Skipped: this customer already exists"].Value,
        _ => localizer["Error"].Value,
    };

    /// <summary>One sentence for the report file: the result and, for an error, what is wrong.</summary>
    private string Describe(ImportRowPlan row) =>
        row.Action == ImportAction.Error
            ? ActionText(row.Action) + ": " + string.Join(" ", row.Issues.Select(IssueText))
            : ActionText(row.Action);
}
