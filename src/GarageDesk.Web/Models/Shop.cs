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
}
