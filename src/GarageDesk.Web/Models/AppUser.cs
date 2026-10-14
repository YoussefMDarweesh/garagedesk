using Microsoft.AspNetCore.Identity;

namespace GarageDesk.Web.Models;

/// <summary>A login. ShopId is null until the owner has created their shop.</summary>
public class AppUser : IdentityUser
{
    public int? ShopId { get; set; }
    public Shop? Shop { get; set; }
}
