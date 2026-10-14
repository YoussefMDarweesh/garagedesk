namespace GarageDesk.Web.Models;

/// <summary>
/// Implement this on every entity that belongs to one shop (Customer, Vehicle, JobCard, ...).
/// ApplicationDbContext then automatically filters reads to the current shop and stamps ShopId on inserts.
/// </summary>
public interface IShopOwned
{
    int ShopId { get; set; }
}
