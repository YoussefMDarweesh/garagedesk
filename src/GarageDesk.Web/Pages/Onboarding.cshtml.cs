using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages;

/// <summary>What the "first customer and car" step posts: one person and one car, in one form.</summary>
public class OnboardingCarInput
{
    [Required, StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required, StringLength(30)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = "";

    [Required, StringLength(20)]
    [Display(Name = "Plate number")]
    public string Plate { get; set; } = "";

    [Required, StringLength(50)]
    [Display(Name = "Make")]
    public string Make { get; set; } = "";

    [Required, StringLength(50)]
    [Display(Name = "Model")]
    public string Model { get; set; } = "";

    [Range(1950, 2100)]
    [Display(Name = "Year")]
    public int Year { get; set; }

    [Range(0, 5_000_000)]
    [Display(Name = "Mileage (km)")]
    public int Mileage { get; set; }
}

/// <summary>
/// The setup wizard a new owner is sent to right after creating the shop: brand it, add the first customer and car,
/// open the first job card. Every step can be skipped and the whole thing can be reopened at any time; what is done
/// is read from the shop's real data (see SetupProgress), so there is nothing to get out of step.
/// </summary>
[Authorize(Policy = Policies.Owner)]
public class OnboardingModel(
    ApplicationDbContext db,
    ICurrentStaff currentStaff,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public const int LastStep = 4;

    public int Step { get; private set; } = 1;

    public SetupProgress Progress { get; private set; } = new(false, false, false, false, false, false);

    public bool HasLogo { get; private set; }

    public string Accent { get; private set; } = Accents.Default;

    public decimal? VatPercent { get; private set; }

    /// <summary>The form values of the customer-and-car step (kept when the step is shown again with errors).</summary>
    public OnboardingCarInput Input { get; private set; } = new() { Year = DateTime.UtcNow.Year };

    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? step)
    {
        await LoadAsync();
        Step = step is >= 1 and <= LastStep ? step.Value : Progress.NextWizardStep;

        // Step 3 needs a car and step 4 means "all done": a link that jumps ahead lands on the first step that still has work.
        if (Step >= 3 && !Progress.HasVehicle)
        {
            Step = 2;
        }
        else if (Step == LastStep && !Progress.HasJob)
        {
            Step = 3;
        }

        await LoadStepDataAsync();
        return Page();
    }

    /// <summary>Step 1: logo (optional), accent colour and VAT percent (optional), all in one go.</summary>
    public async Task<IActionResult> OnPostBrandAsync(IFormFile? upload, string? accent, decimal? vatPercent)
    {
        if (vatPercent is < 0 or > 100)
        {
            return await ShowStepAsync(1, localizer["Enter a VAT percent between 0 and 100."].Value);
        }

        if (accent is not null && !Accents.IsPreset(accent))
        {
            return await ShowStepAsync(1, localizer["Choose one of the colours."].Value);
        }

        byte[]? logoBytes = null;
        string? logoType = null;
        if (upload is { Length: > 0 })
        {
            if (upload.Length > ShopLogoImage.MaxBytes)
            {
                return await ShowStepAsync(1, localizer["The image is too large. The maximum size is 512 KB."].Value);
            }

            logoBytes = new byte[upload.Length];
            await using (var stream = upload.OpenReadStream())
            {
                await stream.ReadExactlyAsync(logoBytes);
            }

            // Judged by content, never by the file name or the type the browser claims.
            logoType = ShopLogoImage.Detect(logoBytes);
            if (logoType is null)
            {
                return await ShowStepAsync(1, localizer["Only PNG, JPEG or WebP images are allowed."].Value);
            }
        }

        var shop = await db.Shops.FirstOrDefaultAsync(); // limited to the signed-in user's shop by the query filter
        if (shop is null)
        {
            return NotFound();
        }

        if (accent is not null)
        {
            shop.AccentColor = accent == Accents.Default ? null : accent;
        }
        shop.VatPercent = vatPercent is > 0 ? Math.Round(vatPercent.Value, 2) : null;

        if (logoBytes is not null)
        {
            var logo = await db.ShopLogos.FirstOrDefaultAsync();
            if (logo is null)
            {
                logo = new ShopLogo(); // ShopId is filled in by the DbContext
                db.ShopLogos.Add(logo);
            }
            logo.Bytes = logoBytes;
            logo.ContentType = logoType!;
            logo.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // The navbar reads the logo and the accent from the login cookie, so re-issue it.
        var user = await userManager.GetUserAsync(User);
        if (user is not null)
        {
            await signInManager.RefreshSignInAsync(user);
        }

        return RedirectToPage(new { step = 2 });
    }

    /// <summary>
    /// Step 2: the first customer and their car. A customer is found by phone number, so posting the step twice
    /// (or entering a second car for the same person) never creates a duplicate person.
    /// </summary>
    public async Task<IActionResult> OnPostCustomerAsync([Bind(Prefix = "Input")] OnboardingCarInput input)
    {
        Input = input;

        if (!EgyptianPhone.TryNormalize(input.Phone, out var phone)
            && (!ModelState.TryGetValue("Input.Phone", out var entry) || entry.Errors.Count == 0))
        {
            ModelState.AddModelError("Input.Phone", localizer["Enter a valid Egyptian phone number, for example 0101 234 5678."]);
        }

        var plateKey = PlateNormalizer.Key(input.Plate);
        if (!string.IsNullOrWhiteSpace(input.Plate) && plateKey.Length == 0)
        {
            ModelState.AddModelError("Input.Plate", localizer["Enter a valid plate number."]);
        }

        if (!ModelState.IsValid)
        {
            return await ShowStepAsync(2);
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone);
        var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.PlateKey == plateKey);
        if (existing is not null)
        {
            // The same car for the same person is "already done" (a double click, a refresh); anyone else's plate is a mistake.
            if (customer is not null && existing.CustomerId == customer.Id)
            {
                return RedirectToPage(new { step = 3 });
            }

            ModelState.AddModelError("Input.Plate", localizer["A vehicle with this plate number already exists in your shop."]);
            return await ShowStepAsync(2);
        }

        if (customer is null)
        {
            customer = new Customer { Name = input.Name.Trim(), Phone = phone };
            db.Customers.Add(customer); // ShopId is filled in by the DbContext
        }

        db.Vehicles.Add(new Vehicle
        {
            Customer = customer,
            Plate = input.Plate.Trim(),
            PlateKey = plateKey,
            Make = input.Make.Trim(),
            Model = input.Model.Trim(),
            Year = input.Year,
            Mileage = input.Mileage,
        });
        await db.SaveChangesAsync(); // customer and car are saved together or not at all

        return RedirectToPage(new { step = 3 });
    }

    /// <summary>Step 3: the first job card for one of the shop's own cars.</summary>
    public async Task<IActionResult> OnPostJobAsync(int vehicleId, string? notes)
    {
        // Another shop's vehicle id is simply not found here (query filter). Tracked: the visit may raise its mileage.
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return await ShowStepAsync(3, localizer["Choose the vehicle."].Value);
        }

        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text is { Length: > 2000 })
        {
            return await ShowStepAsync(3, localizer["The notes are too long (2000 characters at most)."].Value);
        }

        var job = new JobCard
        {
            VehicleId = vehicle.Id,
            DateIn = ShopClock.Today(),
            MileageAtVisit = vehicle.Mileage,
            Notes = text,
            Status = JobStatus.Booked,
            VatPercent = await db.Shops.Select(s => s.VatPercent).FirstOrDefaultAsync(),
        };
        db.JobCards.Add(job);
        JobAudit.Add(db, await currentStaff.GetAsync(), job, JobAuditKind.Created);
        await db.SaveChangesAsync();

        return RedirectToPage("/Jobs/Details", new { id = job.Id });
    }

    private async Task<IActionResult> ShowStepAsync(int step, string? translatedError = null)
    {
        if (translatedError is not null)
        {
            ModelState.AddModelError(string.Empty, translatedError);
        }

        await LoadAsync();
        Step = step;
        await LoadStepDataAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Progress = await SetupProgress.LoadAsync(db);
        HasLogo = Progress.HasLogo;
        var shop = await db.Shops.AsNoTracking().Select(s => new { s.AccentColor, s.VatPercent }).FirstOrDefaultAsync();
        Accent = Accents.IsPreset(shop?.AccentColor) ? shop!.AccentColor! : Accents.Default;
        VatPercent = shop?.VatPercent;
    }

    private async Task LoadStepDataAsync()
    {
        if (Step != 3)
        {
            return;
        }

        // The newest cars first, so the car just added in step 2 is the one pre-selected.
        var vehicles = await db.Vehicles.AsNoTracking()
            .OrderByDescending(v => v.Id)
            .Select(v => new { v.Id, v.Plate, v.Make, v.Model, Owner = v.Customer!.Name })
            .Take(100)
            .ToListAsync();
        VehicleOptions = vehicles
            .Select(v => new SelectListItem($"{v.Plate} · {v.Make} {v.Model} ({v.Owner})", v.Id.ToString()))
            .ToList();
    }
}
