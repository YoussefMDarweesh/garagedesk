using System.ComponentModel.DataAnnotations;
using GarageDesk.Web.Models;

namespace GarageDesk.Web.Pages.Jobs;

public class PaymentInput
{
    [Display(Name = "Amount")]
    [Range(0.01, 9999999999, ErrorMessage = "The amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Display(Name = "Payment method")]
    public PaymentMethod Method { get; set; }

    [Display(Name = "Note")]
    [StringLength(200)]
    public string? Note { get; set; }
}
