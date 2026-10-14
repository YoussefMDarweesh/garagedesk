using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Pages.Vehicles;

public class VehicleInput
{
    [Display(Name = "Owner")]
    public int CustomerId { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "Plate number")]
    public string Plate { get; set; } = "";

    [StringLength(17)]
    [Display(Name = "VIN (optional)")]
    public string? Vin { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "Make")]
    public string Make { get; set; } = "";

    [Required, StringLength(50)]
    [Display(Name = "Model")]
    public string Model { get; set; } = "";

    [Range(1950, 2100)]
    [Display(Name = "Year")]
    public int Year { get; set; }

    [Range(0, 5_000_000)]
    [Display(Name = "Mileage (km)")]
    public int Mileage { get; set; }
}
