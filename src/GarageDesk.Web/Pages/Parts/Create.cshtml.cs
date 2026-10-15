using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>Add a part to the catalog. Owners only: the form carries the cost price.</summary>
[Authorize(Policy = Policies.Owner)]
public class CreateModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer)
    : PartFormPageModel(db, localizer)
{
    public override bool IsNew => true;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await ValidateAsync(exceptId: null))
        {
            return Page();
        }

        var part = Build();
        part.CreatedAtUtc = DateTime.UtcNow;
        Db.Parts.Add(part);

        // The quantity it starts with is the first line of its ledger, so the quantity is always the sum of movements.
        var opening = JobTotals.Round(Input.OpeningQuantity);
        if (opening > 0)
        {
            StockLedger.Apply(Db, part, StockMovementKind.Opening, opening, await currentStaff.GetAsync(), note: "Opening stock");
        }

        await Db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
