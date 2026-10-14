namespace GarageDesk.Web.Models;

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
    Wallet = 3,
}

/// <summary>Money received against one job card: a deposit, an instalment or the final payment.</summary>
public class Payment : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int JobCardId { get; set; }

    public JobCard? JobCard { get; set; }

    /// <summary>Always greater than zero, in EGP, to the piastre.</summary>
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>The day the money was received.</summary>
    public DateOnly PaidOn { get; set; }

    public string? Note { get; set; }
}

public enum PaymentState
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2,
}
