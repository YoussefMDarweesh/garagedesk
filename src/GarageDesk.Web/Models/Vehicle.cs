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

    /// <summary>
    /// Records an odometer reading taken at a visit. The vehicle's current mileage only ever goes up:
    /// an older or mistyped lower reading on a job card must not wind it back.
    /// </summary>
    /// <returns>True if the vehicle's mileage changed.</returns>
    public bool RaiseMileageTo(int reading)
    {
        if (reading <= Mileage)
        {
            return false;
        }

        Mileage = reading;
        return true;
    }
}
