using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Models;

/// <summary>A workshop. This is the tenant: everything else belongs to exactly one shop.</summary>
public class Shop
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, StringLength(30)]
    public string Phone { get; set; } = "";

    [Required, StringLength(200)]
    public string Address { get; set; } = "";

    /// <summary>One of the Accents presets, or null for the default (orange).</summary>
    public string? AccentColor { get; set; }

    /// <summary>VAT percent added to job cards, or null when the shop does not charge VAT.</summary>
    public decimal? VatPercent { get; set; }
}
