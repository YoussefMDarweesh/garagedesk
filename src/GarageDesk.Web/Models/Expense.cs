using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Models;

/// <summary>A heading expenses are filed under (Rent, Salaries...). The owner can add and rename them; used ones are archived, not deleted.</summary>
public class ExpenseCategory : IShopOwned
{
    /// <summary>The headings every shop starts with. Stored in English; shown translated.</summary>
    public static readonly string[] Defaults = ["Rent", "Salaries", "Parts purchase", "Utilities", "Other"];

    public int Id { get; set; }

    public int ShopId { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;
}

/// <summary>Money the shop spent. Part of the profit report and the daily cash close.</summary>
public class Expense : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    /// <summary>The day it was paid.</summary>
    public DateOnly Date { get; set; }

    public int CategoryId { get; set; }

    public ExpenseCategory? Category { get; set; }

    /// <summary>In EGP, to the piastre, always greater than zero.</summary>
    public decimal Amount { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    /// <summary>True when the money came out of the cash drawer (it then reduces the day's expected cash).</summary>
    public bool PaidFromCash { get; set; } = true;

    public string? CreatedByUserId { get; set; }

    [StringLength(100)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The end-of-day count of the cash drawer, with the figures as they were when it was saved. Immutable: once
/// saved it is never changed or removed (the DbContext refuses it); a correction is a new, later close.
/// </summary>
public class CashClose : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    /// <summary>The business day this close is for.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Cash already in the drawer at the start of the day.</summary>
    public decimal OpeningCash { get; set; }

    public decimal CashReceived { get; set; }

    public decimal CardReceived { get; set; }

    public decimal TransferReceived { get; set; }

    public decimal WalletReceived { get; set; }

    /// <summary>All expenses dated this day.</summary>
    public decimal ExpensesTotal { get; set; }

    /// <summary>The part of the expenses paid from the drawer.</summary>
    public decimal CashExpenses { get; set; }

    /// <summary>Opening cash + cash received - cash expenses.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>What the owner counted.</summary>
    public decimal CountedCash { get; set; }

    /// <summary>Counted minus expected: negative means the drawer is short.</summary>
    public decimal Difference { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    public string? ClosedByUserId { get; set; }

    [StringLength(100)]
    public string ClosedByName { get; set; } = "";

    public DateTime ClosedAtUtc { get; set; }
}
