namespace GarageDesk.Web.Services;

/// <summary>What the <c>_ShopLogo</c> partial needs to draw a shop's logo on its "plate" (see site.css, .logo-plate).</summary>
/// <param name="Src">The image URL: the shop's own /shop/logo?v=... or the share page's logo handler. The same URL is reused everywhere on a page, so the browser fetches the image once.</param>
/// <param name="Alt">The shop name.</param>
/// <param name="Size">Which size the plate has: rail, sm, hero or card.</param>
public sealed record ShopLogoView(string Src, string Alt, string Size = "card");
