namespace GarageDesk.Web.Models;

public class Customer : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Always stored normalized, as +20 followed by the national number (see EgyptianPhone).</summary>
    public string Phone { get; set; } = "";

    public string? Notes { get; set; }

    public List<Vehicle> Vehicles { get; set; } = [];
}
