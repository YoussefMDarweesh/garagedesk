namespace GarageDesk.Web.Services;

/// <summary>
/// Decides what an uploaded file really is by reading its first bytes. The file name and the
/// content type the browser sent are ignored, because both can be faked. SVG is deliberately not
/// supported: it can carry scripts.
/// </summary>
public static class ShopLogoImage
{
    public const int MaxBytes = 512 * 1024;

    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string WebP = "image/webp";

    /// <summary>Returns the image type, or null if the bytes are not a PNG, JPEG or WebP.</summary>
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        // PNG: 8-byte signature, then the first chunk must be IHDR.
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length >= 16 && data[..8].SequenceEqual(pngSignature) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return Png;
        }

        // JPEG: starts with the SOI marker followed by another marker.
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return Jpeg;
        }

        // WebP: "RIFF" <size> "WEBP" then a VP8 / VP8L / VP8X chunk.
        if (data.Length >= 16
            && data[..4].SequenceEqual("RIFF"u8)
            && data.Slice(8, 4).SequenceEqual("WEBP"u8)
            && data.Slice(12, 3).SequenceEqual("VP8"u8))
        {
            return WebP;
        }

        return null;
    }
}
