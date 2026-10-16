using System.IO.Compression;
using System.Text;

namespace GarageDesk.Tests;

/// <summary>
/// Small, genuine image files for the image-size and logo tests. The 64 x 48 PNG, JPEG (with a colour-profile segment
/// in front of the frame header, like real camera and browser files) and WebP (VP8X) were encoded by a browser; the 1 x 1
/// WebP files are the well-known smallest lossy and lossless WebP. Bigger PNGs are built by <see cref="Png"/>.
/// </summary>
public static class TinyImages
{
    public static readonly byte[] Png64x48 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAwCAYAAAChS3wfAAAAw0lEQVR4AeyUsQ2AMAwEowwBLWOxB0sxFi0jUEHvxoqsKCZ/SCksQ7D/z67PsbzKpxbxBwHEASgQAAHiCjAC4gCwBBkBRiCowHpuZeQJls8OYAdEEfrb97ZeCLCKqMUQoOa47RcCrCJqMQSoOW77hQCriFoMAWqO234hwCqiFkPA7I57/UGAp9DseQiY3WGvvzAB936Vkcdr0MuHBfB+kD2PANkd6l0fBPRWOPv9EJDdod71QUBvhbPfDwHZHWqtr/X9DwAA//8BdS+pAAAABklEQVQDAFRXALBQn3giAAAAAElFTkSuQmCC");

    public static readonly byte[] Jpeg64x48 = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/4gHYSUNDX1BST0ZJTEUAAQEAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADb/2wBDAAoHBwgHBgoICAgLCgoLDhgQDg0NDh0VFhEYIx8lJCIfIiEmKzcvJik0KSEiMEExNDk7Pj4+JS5ESUM8SDc9Pjv/2wBDAQoLCw4NDhwQEBw7KCIoOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozv/wAARCAAwAEADASIAAhEBAxEB/8QAFwABAQEBAAAAAAAAAAAAAAAAAAYHBP/EABgQAQADAQAAAAAAAAAAAAAAAAAWZaPi/8QAFwEBAQEBAAAAAAAAAAAAAAAAAAUHBv/EABoRAAEFAQAAAAAAAAAAAAAAAAABBBVSodH/2gAMAwEAAhEDEQA/AO0BxB3gEhPazfkntZvyoxbump0nSbS+LwrxIT2s35J7Wb8kW7pqdEm0vi8K8BOKIAAZCA0Mz0AANeAZ4aEAAEhArPDogVnh0rxRlHd8ThOjGlNXpIQKzw6IFZ4dK8JR3fE4IxpTV6AE4on/2Q==");

    /// <summary>A WebP in the extended (VP8X) layout with a colour profile, as a browser writes it.</summary>
    public static readonly byte[] WebPExtended64x48 = Convert.FromBase64String(
        "UklGRnYCAABXRUJQVlA4WAoAAAAgAAAAPwAALwAASUNDUMgBAAAAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADZWUDggiAAAAHAHAJ0BKkAAMAA+iTSUSCUioiE3/jgAoBEJZADOra7+AaxVwD5AJgB/APsAvP+7AP0A7f8J1de/NfjY/ALDtk3eitJAAP7e1jqcci1lH7V3rgNaSYv9kUn6Rgqy5UZ9T1gwiXuJ+h6nbweI3K0FPaTXl5ZjcIIAHCSpwtHfV0FVj5pUxNAAAAA=");

    /// <summary>The smallest lossy WebP (plain VP8 chunk), 1 x 1.</summary>
    public static readonly byte[] WebPLossy1x1 = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    /// <summary>The smallest lossless WebP (VP8L chunk), 1 x 1.</summary>
    public static readonly byte[] WebPLossless1x1 = Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==");

    /// <summary>
    /// A valid PNG of the given size, drawn in a single flat colour (it compresses to a few hundred bytes, so even a
    /// 1024 x 1024 logo is far below the 512 KB upload limit).
    /// </summary>
    public static byte[] Png(int width, int height)
    {
        var raw = new byte[height * (1 + width * 3)]; // filter byte 0 + RGB per row
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            for (var x = 0; x < width; x++)
            {
                raw[row + 1 + x * 3] = 0xF9;     // R
                raw[row + 1 + x * 3 + 1] = 0x73; // G
                raw[row + 1 + x * 3 + 2] = 0x16; // B
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Only the first bytes of a PNG: signature and a valid IHDR with any size you like (no pixel data). For header-parser tests.</summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", ihdr);
        return png.ToArray();
    }

    /// <summary>A plain lossy WebP header (RIFF + VP8 chunk) claiming the given size.</summary>
    public static byte[] WebPLossyHeader(int width, int height)
    {
        var data = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
        BitConverter.GetBytes(22).CopyTo(data, 4);
        Encoding.ASCII.GetBytes("WEBPVP8 ").CopyTo(data, 8);
        BitConverter.GetBytes(10).CopyTo(data, 16);
        data[23] = 0x9D; data[24] = 0x01; data[25] = 0x2A;
        data[26] = (byte)(width & 0xFF); data[27] = (byte)((width >> 8) & 0x3F);
        data[28] = (byte)(height & 0xFF); data[29] = (byte)((height >> 8) & 0x3F);
        return data;
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(crcInput));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
