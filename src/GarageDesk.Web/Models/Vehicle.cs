namespace GarageDesk.Web.Models;

public class Vehicle : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>The plate as the shop typed it, for display.</summary>
    public string Plate { get; set; } = "";

    /// <summary>The plate reduced to letters and digits (see PlateNormalizer), used for searching.</summary>
    public string PlateKey { get; set; } = "";

    public string? Vin { get; set; }

    public string Make { get; set; } = "";

    public string Model { get; set; } = "";

    public int Year { get; set; }

    /// <summary>Current mileage in kilometres.</summary>
    public int Mileage { get; set; }
}
