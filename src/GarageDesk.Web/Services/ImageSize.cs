namespace GarageDesk.Web.Services;

/// <summary>
/// Reads an image's pixel width and height straight from its file header: PNG (IHDR), JPEG (the SOF marker) and WebP
/// (VP8, VP8L or VP8X). No image library and no decoding, so it is cheap and cannot be tricked into allocating a
/// huge bitmap. It only informs the owner ("this logo is small"); it never accepts or rejects an upload.
/// It never throws: a truncated, malformed or unknown file simply gives <c>null</c>.
/// </summary>
public static class ImageSize
{
    /// <summary>A logo whose shorter side is below this many pixels gets a gentle "may look blurry" hint.</summary>
    public const int SmallThreshold = 256;

    /// <summary>The pixel size, or null when the bytes are not a PNG, JPEG or WebP whose header can be read.</summary>
    public static (int Width, int Height)? TryRead(ReadOnlySpan<byte> data)
    {
        try
        {
            var size = ReadPng(data) ?? ReadWebP(data) ?? ReadJpeg(data);
            return size is { Item1: > 0, Item2: > 0 } found ? (found.Item1, found.Item2) : null;
        }
        catch (Exception)
        {
            // Belt and braces: every read below is bounds-checked, but a header parser must never take a page down.
            return null;
        }
    }

    /// <summary>True when the shorter side is under <see cref="SmallThreshold"/>.</summary>
    public static bool IsSmall(int width, int height) => Math.Min(width, height) < SmallThreshold;

    // ---- PNG: 8-byte signature, then the IHDR chunk: length(4) "IHDR" width(4) height(4), big-endian.
    private static (int, int)? ReadPng(ReadOnlySpan<byte> d)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 24 || !d[..8].SequenceEqual(signature) || !d.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BigEndian32(d, 16);
        var height = BigEndian32(d, 20);
        return width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue ? ((int)width, (int)height) : null;
    }

    // ---- JPEG: walk the marker segments until a Start Of Frame (SOF0..SOF15, not DHT/JPG/DAC) gives the size.
    private static (int, int)? ReadJpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        var i = 2;
        while (i + 3 < d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null; // not a marker where one must be: corrupt
            }

            while (i < d.Length && d[i] == 0xFF)
            {
                i++; // fill bytes
            }

            if (i >= d.Length)
            {
                return null;
            }

            var marker = d[i++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                continue; // standalone markers carry no length (SOI, EOI, RSTn, TEM)
            }

            if (marker == 0xDA)
            {
                return null; // start of scan reached without a frame header
            }

            if (i + 2 > d.Length)
            {
                return null;
            }

            var length = (d[i] << 8) | d[i + 1];
            var isFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrame)
            {
                // length(2) precision(1) height(2) width(2)
                if (length < 8 || i + 7 > d.Length)
                {
                    return null;
                }

                var height = (d[i + 3] << 8) | d[i + 4];
                var width = (d[i + 5] << 8) | d[i + 6];
                return (width, height);
            }

            if (length < 2)
            {
                return null;
            }

            i += length;
        }

        return null;
    }

    // ---- WebP: "RIFF" size "WEBP", then the first chunk says which flavour it is.
    private static (int, int)? ReadWebP(ReadOnlySpan<byte> d)
    {
        if (d.Length < 30 || !d[..4].SequenceEqual("RIFF"u8) || !d.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var kind = d.Slice(12, 4);

        if (kind.SequenceEqual("VP8 "u8))
        {
            // Lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width and height (little-endian, top 2 bits are scale).
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A)
            {
                return null;
            }

            return ((d[26] | (d[27] << 8)) & 0x3FFF, (d[28] | (d[29] << 8)) & 0x3FFF);
        }

        if (kind.SequenceEqual("VP8L"u8))
        {
            // Lossless: signature byte 0x2F, then 14 bits of (width - 1) and 14 bits of (height - 1).
            if (d[20] != 0x2F)
            {
                return null;
            }

            var bits = d[21] | (d[22] << 8) | (d[23] << 16) | ((uint)d[24] << 24);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (kind.SequenceEqual("VP8X"u8))
        {
            // Extended: flags(1) reserved(3), then 24-bit (canvas width - 1) and (canvas height - 1), little-endian.
            var width = (d[24] | (d[25] << 8) | (d[26] << 16)) + 1;
            var height = (d[27] | (d[28] << 8) | (d[29] << 16)) + 1;
            return (width, height);
        }

        return null;
    }

    private static long BigEndian32(ReadOnlySpan<byte> d, int offset) =>
        ((long)d[offset] << 24) | ((long)d[offset + 1] << 16) | ((long)d[offset + 2] << 8) | d[offset + 3];
}
