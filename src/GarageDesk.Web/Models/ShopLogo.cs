namespace GarageDesk.Web.Models;

/// <summary>
/// A shop's logo, stored in the database (Azure's app disk isn't durable). One per shop.
/// It lives in its own table so the image bytes are only read when the logo itself is requested.
/// </summary>
public class ShopLogo : IShopOwned
{
    public int Id { get; set; }

    public int ShopId { get; set; }

    public byte[] Bytes { get; set; } = [];

    /// <summary>Always one of image/png, image/jpeg, image/webp, decided from the file's bytes, never from the upload's claimed type.</summary>
    public string ContentType { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; }
}
