using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Models;

namespace GarageDesk.Web.Pages.Jobs;

/// <summary>The job card form (add and edit). VehicleId is only used when adding.</summary>
public class JobInput
{
    [Display(Name = "Vehicle")]
    public int VehicleId { get; set; }

    [Display(Name = "Date in")]
    public DateOnly DateIn { get; set; }

    [Range(0, 5_000_000)]
    [Display(Name = "Mileage at visit (km)")]
    public int MileageAtVisit { get; set; }

    [Range(0, 10_000_000)]
    [Display(Name = "Discount (EGP)")]
    public decimal? DiscountAmount { get; set; }

    [Range(0, 100)]
    [Display(Name = "Discount (%)")]
    public decimal? DiscountPercent { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

/// <summary>The "add a line" form on a job card.</summary>
public class JobLineInput
{
    [Display(Name = "Type")]
    public JobLineType Type { get; set; } = JobLineType.Part;

    [Required, StringLength(200)]
    [Display(Name = "Description")]
    public string Description { get; set; } = "";

    [Range(0.01, 100_000)]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 10_000_000)]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }
}
