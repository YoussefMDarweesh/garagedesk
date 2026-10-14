using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Pages.Reminders;

public class ReminderInput
{
    [Display(Name = "Vehicle")]
    public int VehicleId { get; set; }

    [Required, StringLength(60)]
    [Display(Name = "What is due")]
    public string Type { get; set; } = "";

    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [Range(0, 5_000_000)]
    [Display(Name = "Due at mileage (km)")]
    public int? DueMileage { get; set; }
}
