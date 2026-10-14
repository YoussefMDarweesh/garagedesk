namespace GarageDesk.Web.Services;

/// <summary>
/// The one place where a job card's money is worked out, so the job card page, the invoice, balances and the
/// receivables list can never disagree. Order: subtotal (sum of rounded line totals), then the discount on the subtotal,
/// then VAT on the discounted amount. Every step is rounded half away from zero to the piastre.
/// </summary>
public static class JobTotals
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>A fixed amount, or a percent of the subtotal; never more than the subtotal, never negative.</summary>
    public static decimal Discount(decimal subtotal, decimal? amount, decimal? percent)
    {
        var value = percent is > 0
            ? Round(subtotal * percent.Value / 100m)
            : amount is > 0 ? Round(amount.Value) : 0m;

        return Math.Clamp(value, 0m, Math.Max(subtotal, 0m));
    }

    public static decimal Vat(decimal taxable, decimal? vatPercent) =>
        vatPercent is > 0 ? Round(taxable * vatPercent.Value / 100m) : 0m;

    public static decimal Total(decimal subtotal, decimal? discountAmount, decimal? discountPercent, decimal? vatPercent)
    {
        var discount = Discount(subtotal, discountAmount, discountPercent);
        var taxable = subtotal - discount;
        return taxable + Vat(taxable, vatPercent);
    }
}
