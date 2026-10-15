using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Services;

public static class ExpenseCategories
{
    /// <summary>The name that stock purchases are filed under.</summary>
    public const string PartsPurchase = "Parts purchase";

    /// <summary>
    /// The shop's categories, creating the five defaults the first time they are needed. Safe to call from two
    /// requests at once: the unique (shop, name) index stops duplicates and the loser just reads what the winner made.
    /// </summary>
    public static async Task<List<ExpenseCategory>> EnsureDefaultsAsync(ApplicationDbContext db)
    {
        if (!await db.ExpenseCategories.AnyAsync())
        {
            foreach (var name in ExpenseCategory.Defaults)
            {
                db.ExpenseCategories.Add(new ExpenseCategory { Name = name });
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return await db.ExpenseCategories.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }

    /// <summary>The default headings are stored in English and shown translated; names the owner typed are shown as typed.</summary>
    public static string Display(IStringLocalizer localizer, string name) =>
        ExpenseCategory.Defaults.Contains(name) ? localizer[name].Value : name;
}
