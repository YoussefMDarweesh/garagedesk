using GarageDesk.Web.Models;

namespace GarageDesk.Web.Services;

/// <summary>The customer and vehicle searches, kept in one place so tests can check them on both database providers.</summary>
public static class SearchExtensions
{
    /// <summary>One search box, three kinds of search: name, phone, or a plate of any of the customer's cars.</summary>
    public static IQueryable<Customer> Search(this IQueryable<Customer> customers, string? query)
    {
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return customers;
        }

        var name = text.ToLowerInvariant();
        var phone = EgyptianPhone.SearchDigits(text) ?? "";
        var plate = PlateNormalizer.Key(text);

        return customers.Where(c =>
            c.Name.ToLower().Contains(name)
            || (phone != "" && c.Phone.Contains(phone))
            || (plate != "" && c.Vehicles.Any(v => v.PlateKey.Contains(plate))));
    }

    /// <summary>Vehicles by plate, make, model, VIN or owner name.</summary>
    public static IQueryable<Vehicle> Search(this IQueryable<Vehicle> vehicles, string? query)
    {
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return vehicles;
        }

        var lower = text.ToLowerInvariant();
        var plate = PlateNormalizer.Key(text);

        return vehicles.Where(v =>
            (plate != "" && v.PlateKey.Contains(plate))
            || v.Make.ToLower().Contains(lower)
            || v.Model.ToLower().Contains(lower)
            || (v.Vin != null && v.Vin.ToLower().Contains(lower))
            || v.Customer!.Name.ToLower().Contains(lower));
    }
}
