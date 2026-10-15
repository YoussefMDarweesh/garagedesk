using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Money;

/// <summary>The expense category list: add, rename, archive and restore. Owners only.</summary>
[Authorize(Policy = Policies.Owner)]
public class CategoriesModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public List<ExpenseCategory> Categories { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string CategoryName(string name) => ExpenseCategories.Display(localizer, name);

    public async Task OnGetAsync() => Categories = await ExpenseCategories.EnsureDefaultsAsync(db);

    public async Task<IActionResult> OnPostAddAsync(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length is 0 or > 60)
        {
            return Fail("Enter a name of up to 60 characters.");
        }

        var existing = await ExpenseCategories.EnsureDefaultsAsync(db);
        if (existing.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("You already have a category with this name.");
        }

        db.ExpenseCategories.Add(new ExpenseCategory { Name = clean });
        await db.SaveChangesAsync();
        StatusMessage = localizer["Category added."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string? name)
    {
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound();
        }

        var clean = (name ?? "").Trim();
        if (clean.Length is 0 or > 60)
        {
            return Fail("Enter a name of up to 60 characters.");
        }

        var others = await db.ExpenseCategories.AsNoTracking().Where(c => c.Id != id).Select(c => c.Name).ToListAsync();
        if (others.Any(n => string.Equals(n, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("You already have a category with this name.");
        }

        category.Name = clean;
        await db.SaveChangesAsync();
        StatusMessage = localizer["Category renamed."];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostActiveAsync(int id, bool active)
    {
        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound();
        }

        // Archived categories keep their old expenses; they just stop being offered for new ones.
        category.IsActive = active;
        await db.SaveChangesAsync();
        StatusMessage = active ? localizer["Category restored."] : localizer["Category archived."];
        return RedirectToPage();
    }

    private IActionResult Fail(string englishMessage)
    {
        ErrorMessage = localizer[englishMessage];
        return RedirectToPage();
    }
}
