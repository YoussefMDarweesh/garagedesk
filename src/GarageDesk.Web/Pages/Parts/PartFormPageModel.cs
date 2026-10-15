using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Parts;

/// <summary>What the Add and Edit part pages share: the bound form and the duplicate check.</summary>
public abstract class PartFormPageModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    protected ApplicationDbContext Db { get; } = db;

    [BindProperty]
    public PartInput Input { get; set; } = new();

    /// <summary>True on the Add page (the quantity can be typed); on Edit quantity changes go through the stock page.</summary>
    public abstract bool IsNew { get; }

    protected async Task<bool> ValidateAsync(int? exceptId)
    {
        if (!ModelState.IsValid)
        {
            return false;
        }

        var clash = await PartRules.ClashAsync(Db, Input, exceptId);
        if (clash == nameof(PartInput.Name))
        {
            ModelState.AddModelError("Input.Name", localizer["You already have a part with this name."]);
        }
        else if (clash == nameof(PartInput.Sku))
        {
            ModelState.AddModelError("Input.Sku", localizer["You already have a part with this SKU."]);
        }

        return ModelState.IsValid;
    }

    protected static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected Part Build(Part? part = null)
    {
        part ??= new Part();
        part.Name = Input.Name.Trim();
        part.Sku = TrimToNull(Input.Sku);
        part.Unit = Input.Unit.Trim();
        part.CostPrice = JobTotalsRound(Input.CostPrice);
        part.SellPrice = JobTotalsRound(Input.SellPrice);
        part.LowStockThreshold = Input.LowStockThreshold;
        part.Supplier = TrimToNull(Input.Supplier);
        return part;
    }

    private static decimal JobTotalsRound(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
