using GarageDesk.Web.Services;

namespace GarageDesk.Tests;

/// <summary>
/// brand-logo: the hand-written header parser that tells the owner how many pixels a logo has. It only informs (a
/// "may look blurry" hint); it never accepts or rejects an upload, and it must never throw, whatever the bytes are.
/// </summary>
public class ImageSizeTests
{
    // ---- valid files ----

    [Fact]
    public void A_png_gives_its_width_and_height_from_the_IHDR_chunk()
    {
        Assert.Equal<(int, int)?>((64, 48), ImageSize.TryRead(TinyImages.Png64x48));
        Assert.Equal<(int, int)?>((300, 200), ImageSize.TryRead(TinyImages.Png(300, 200)));
        Assert.Equal<(int, int)?>((1024, 256), ImageSize.TryRead(TinyImages.PngHeaderOnly(1024, 256)));
    }

    [Fact]
    public void A_jpeg_gives_its_size_even_with_a_colour_profile_segment_in_front_of_the_frame_header()
    {
        Assert.Equal<(int, int)?>((64, 48), ImageSize.TryRead(TinyImages.Jpeg64x48));
    }

    [Fact]
    public void A_webp_gives_its_size_in_all_three_layouts()
    {
        Assert.Equal<(int, int)?>((64, 48), ImageSize.TryRead(TinyImages.WebPExtended64x48)); // VP8X
        Assert.Equal<(int, int)?>((1, 1), ImageSize.TryRead(TinyImages.WebPLossy1x1));        // VP8
        Assert.Equal<(int, int)?>((1, 1), ImageSize.TryRead(TinyImages.WebPLossless1x1));     // VP8L
        Assert.Equal<(int, int)?>((320, 240), ImageSize.TryRead(TinyImages.WebPLossyHeader(320, 240)));
    }

    [Fact]
    public void The_size_agrees_with_the_type_detection_used_for_uploads()
    {
        foreach (var file in new[] { TinyImages.Png64x48, TinyImages.Jpeg64x48, TinyImages.WebPExtended64x48 })
        {
            Assert.NotNull(ShopLogoImage.Detect(file));
            Assert.Equal<(int, int)?>((64, 48), ImageSize.TryRead(file));
        }
    }

    // ---- malformed and truncated files: null, never an exception ----

    [Fact]
    public void Empty_and_unknown_bytes_give_null()
    {
        Assert.Null(ImageSize.TryRead([]));
        Assert.Null(ImageSize.TryRead([0x00]));
        Assert.Null(ImageSize.TryRead("this is not an image at all, just some text"u8));
        Assert.Null(ImageSize.TryRead([0xFF, 0xD8])); // a JPEG start and nothing else
        Assert.Null(ImageSize.TryRead("RIFF"u8));
    }

    [Fact]
    public void A_png_with_the_wrong_first_chunk_or_a_zero_size_gives_null()
    {
        var notIhdr = (byte[])TinyImages.Png64x48.Clone();
        notIhdr[12] = (byte)'X'; // "XHDR"
        Assert.Null(ImageSize.TryRead(notIhdr));

        Assert.Null(ImageSize.TryRead(TinyImages.PngHeaderOnly(0, 100)));
        Assert.Null(ImageSize.TryRead(TinyImages.PngHeaderOnly(100, 0)));
    }

    [Fact]
    public void A_jpeg_whose_segments_lie_about_their_length_gives_null()
    {
        // SOI, then an APP0 segment claiming 0xFFFF bytes: the walk must stop at the end of the data.
        Assert.Null(ImageSize.TryRead([0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00]));
        // A segment length of 0 or 1 would loop forever if it were trusted.
        Assert.Null(ImageSize.TryRead([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0xFF, 0xE1, 0x00, 0x01, 0x00, 0x00]));
        // Start of scan before any frame header.
        Assert.Null(ImageSize.TryRead([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]));
    }

    [Fact]
    public void Every_truncation_of_every_valid_file_either_reads_the_right_size_or_gives_null_and_never_throws()
    {
        var files = new[]
        {
            TinyImages.Png64x48, TinyImages.Jpeg64x48, TinyImages.WebPExtended64x48,
            TinyImages.WebPLossy1x1, TinyImages.WebPLossless1x1, TinyImages.Png(300, 200),
        };

        foreach (var file in files)
        {
            var expected = ImageSize.TryRead(file);
            Assert.NotNull(expected);

            for (var length = 0; length < file.Length; length++)
            {
                var prefix = file.AsSpan(0, length);
                var size = ImageSize.TryRead(prefix); // must not throw
                Assert.True(size is null || size == expected, $"A {length}-byte prefix gave {size}, expected {expected} or null.");
            }
        }
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(12345);
        var buffer = new byte[512];
        for (var round = 0; round < 500; round++)
        {
            random.NextBytes(buffer);
            // sometimes start like a real format so the deeper branches run
            switch (round % 3)
            {
                case 0: new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(buffer, 0); break;
                case 1: new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(buffer, 0); break;
                case 2: "RIFF\0\0\0\0WEBPVP8X"u8.ToArray().CopyTo(buffer, 0); break;
            }

            _ = ImageSize.TryRead(buffer);
        }
    }

    // ---- the "small logo" rule ----

    [Theory]
    [InlineData(255, 1000, true)]
    [InlineData(1000, 255, true)]
    [InlineData(256, 256, false)]
    [InlineData(1024, 256, false)]
    [InlineData(96, 96, true)]
    [InlineData(512, 512, false)]
    public void A_logo_is_small_when_its_shorter_side_is_under_256_pixels(int width, int height, bool small)
    {
        Assert.Equal(small, ImageSize.IsSmall(width, height));
    }
}
