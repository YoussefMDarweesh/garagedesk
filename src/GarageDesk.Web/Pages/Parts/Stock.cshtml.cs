using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>Stock received from a supplier.</summary>
public class PurchaseInput
{
    [Range(0.01, 1_000_000)]
    [Display(Name = "Quantity received")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 10_000_000)]
    [Display(Name = "Cost per unit (EGP)")]
    public decimal UnitCost { get; set; }

    [StringLength(200)]
    [Display(Name = "Note")]
    public string? Note { get; set; }

    [Display(Name = "Also record it as an expense (Parts purchase)")]
    public bool RecordExpense { get; set; } = true;
}

/// <summary>A correction after counting the shelf, or a loss. A signed number, with a reason.</summary>
public class AdjustInput
{
    [Range(-1_000_000, 1_000_000)]
    [Display(Name = "Change (use a minus to take stock away)")]
    public decimal Change { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = "";
}

/// <summary>One part's quantity and its full ledger. Receive stock, or correct the count. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class StockModel(ApplicationDbContext db, ICurrentStaff currentStaff, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public Part Part { get; private set; } = null!;

    public List<StockMovement> Movements { get; private set; } = [];

    // Bound only inside their own handlers, so one form never validates the other.
    public PurchaseInput Purchase { get; set; } = new();

    public AdjustInput Adjust { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Purchase.UnitCost = Part.CostPrice;
        return Page();
    }

    public async Task<IActionResult> OnPostPurchaseAsync(int id)
    {
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return NotFound();
        }

        await TryUpdateModelAsync(Purchase, nameof(Purchase));
        if (ModelState.Where(e => e.Key.StartsWith(nameof(Purchase))).Any(e => e.Value?.Errors.Count > 0))
        {
            return Fail(id, "Enter a quantity above zero and a cost of zero or more.");
        }

        var quantity = JobTotals.Round(Purchase.Quantity);
        var cost = JobTotals.Round(Purchase.UnitCost);
        if (quantity <= 0)
        {
            return Fail(id, "Enter a quantity above zero and a cost of zero or more.");
        }

        var actor = await currentStaff.GetAsync();
        StockLedger.Apply(db, part, StockMovementKind.Purchase, quantity, actor, unitCost: cost, note: Purchase.Note?.Trim() ?? "");

        // The latest purchase price becomes the part's cost, for job cards added from now on. Old lines keep theirs.
        part.CostPrice = cost;

        if (Purchase.RecordExpense && quantity * cost > 0)
        {
            var categories = await ExpenseCategories.EnsureDefaultsAsync(db);
            var category = categories.FirstOrDefault(c => c.Name == ExpenseCategories.PartsPurchase && c.IsActive)
                           ?? categories.FirstOrDefault(c => c.IsActive);
            if (category is not null)
            {
                db.Expenses.Add(new Expense
                {
                    Date = ShopClock.Today(),
                    CategoryId = category.Id,
                    Amount = JobTotals.Round(quantity * cost),
                    Note = $"{part.Name} x {quantity:0.##}",
                    PaidFromCash = true,
                    CreatedByUserId = actor?.UserId,
                    CreatedByName = actor?.DisplayName ?? "",
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
        }

        return await SaveAsync(id, "Stock received.");
    }

    public async Task<IActionResult> OnPostAdjustAsync(int id)
    {
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return NotFound();
        }

        await TryUpdateModelAsync(Adjust, nameof(Adjust));
        if (ModelState.Where(e => e.Key.StartsWith(nameof(Adjust))).Any(e => e.Value?.Errors.Count > 0))
        {
            return Fail(id, "Enter the change and a reason.");
        }

        var change = JobTotals.Round(Adjust.Change);
        if (change == 0)
        {
            return Fail(id, "Enter the change and a reason.");
        }

        if (change < 0 && !await StockLedger.AllowsNegativeAsync(db) && part.QuantityOnHand + change < 0)
        {
            return Fail(id, StockLedger.Messages.NegativeNotAllowed);
        }

        StockLedger.Apply(db, part, StockMovementKind.Adjustment, change, await currentStaff.GetAsync(), note: Adjust.Reason.Trim());
        return await SaveAsync(id, "Stock corrected.");
    }

    private async Task<IActionResult> SaveAsync(int id, string successMessage)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(id, StockLedger.Messages.Changed);
        }

        StatusMessage = localizer[successMessage];
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return false;
        }

        Part = part;
        Movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.PartId == id)
            .OrderByDescending(m => m.AtUtc).ThenByDescending(m => m.Id)
            .Take(200)
            .ToListAsync();
        return true;
    }

    private IActionResult Fail(int id, string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage(new { id });
    }
}
