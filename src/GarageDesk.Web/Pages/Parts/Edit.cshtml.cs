using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>Edit a part's details, or archive it. Quantity is not editable here: it only changes through the stock page.</summary>
[Authorize(Policy = Policies.Owner)]
public class EditModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
    : PartFormPageModel(db, localizer)
{
    public override bool IsNew => false;

    public int PartId { get; private set; }

    public bool IsActive { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var part = await Db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return NotFound();
        }

        Fill(part);
        Input = new PartInput
        {
            Name = part.Name,
            Sku = part.Sku,
            Unit = part.Unit,
            CostPrice = part.CostPrice,
            SellPrice = part.SellPrice,
            LowStockThreshold = part.LowStockThreshold,
            Supplier = part.Supplier,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var part = await Db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return NotFound();
        }

        // Quantity is not part of this form; ignore anything posted for it.
        ModelState.Remove("Input.OpeningQuantity");
        if (!await ValidateAsync(exceptId: id))
        {
            Fill(part);
            return Page();
        }

        // Only the details change. Old job cards keep the cost they recorded, so profit history is not rewritten.
        Build(part);
        await Db.SaveChangesAsync();
        StatusMessage = localizer["Part saved."];
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id, bool archive)
    {
        var part = await Db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return NotFound();
        }

        part.IsActive = !archive;
        await Db.SaveChangesAsync();
        StatusMessage = archive
            ? localizer["Part archived. It can no longer be picked on new lines."]
            : localizer["Part restored."];
        return RedirectToPage("Index");
    }

    private void Fill(Part part)
    {
        PartId = part.Id;
        IsActive = part.IsActive;
        QuantityOnHand = part.QuantityOnHand;
    }
}
