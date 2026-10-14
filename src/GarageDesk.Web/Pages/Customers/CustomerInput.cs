using System.ComponentModel.DataAnnotations;

namespace GarageDesk.Web.Pages.Customers;

/// <summary>What the customer form posts. The phone is validated and normalized in the page handler.</summary>
public class CustomerInput
{
    [Required, StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required, StringLength(30)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = "";

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
