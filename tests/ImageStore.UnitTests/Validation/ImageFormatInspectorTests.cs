using System.Buffers.Binary;
using System.Text;
using ImageStore.Application.Images.Validation;
using ImageStore.UnitTests.TestSupport;

namespace ImageStore.UnitTests.Validation;

public sealed class ImageFormatInspectorTests
{
    [Theory]
    [InlineData("sample.jpg", ImageFormat.Jpeg, 16, 12)]
    [InlineData("sample.png", ImageFormat.Png, 20, 10)]
    [InlineData("sample-lossy.webp", ImageFormat.WebP, 24, 18)]     // VP8
    [InlineData("sample-lossless.webp", ImageFormat.WebP, 8, 6)]    // VP8L
    [InlineData("sample-alpha.webp", ImageFormat.WebP, 30, 22)]     // VP8X
    public void Detects_Format_And_Dimensions_Of_Real_Images(string file, ImageFormat format, int width, int height)
    {
        var result = Inspect(TestFiles.Read(file));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(format, result.Format);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
    }

    [Fact]
    public void Detects_Large_Generated_Png()
    {
        var result = Inspect(TestFiles.CreatePng(1920, 1080));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal((1920, 1080), (result.Width, result.Height));
    }

    public static TheoryData<string, byte[]> MaliciousOrUnsupportedContent() => new()
    {
        { "empty", [] },
        { "plain text", Encoding.UTF8.GetBytes("just some text, definitely not an image") },
        { "php script", Encoding.UTF8.GetBytes("<?php system($_GET['cmd']); ?>") },
        { "svg with script", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>") },
        { "html", Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><script>alert(1)</script></body></html>") },
        { "gif", [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0, 0x3B] },
        { "bmp", [.. "BM"u8, .. new byte[40]] },
        { "png signature followed by script", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "<?php echo 1; ?>              "u8] },
        { "jpeg markers only", [0xFF, 0xD8, 0xFF, 0xD9, 0, 0, 0, 0, 0, 0, 0, 0] },
        { "jpeg segment longer than file", [0xFF, 0xD8, 0xFF, 0xE0, 0x7F, 0xFF, 0, 0, 0, 0, 0, 0] },
        { "webp riff size exceeds file", [.. "RIFF"u8, 0xFF, 0xFF, 0, 0, .. "WEBP"u8, .. "VP8 "u8, 10, 0, 0, 0] },
        { "webp unknown chunk", [.. "RIFF"u8, 22, 0, 0, 0, .. "WEBP"u8, .. "ABCD"u8, 10, 0, 0, 0, .. new byte[10]] },
    };

    [Theory]
    [MemberData(nameof(MaliciousOrUnsupportedContent))]
    public void Rejects_Malicious_Unsupported_And_Malformed_Content(string description, byte[] content)
    {
        var result = Inspect(content);

        Assert.False(result.IsValid, $"'{description}' should be rejected");
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Rejects_Truncated_Images()
    {
        var png = TestFiles.Read("sample.png");
        var jpeg = TestFiles.Read("sample.jpg");

        Assert.False(Inspect(png[..30]).IsValid);
        Assert.False(Inspect(jpeg[..20]).IsValid); // Cut inside the APP0 segment, before the frame header.
    }

    [Fact]
    public void Rejects_Png_With_Corrupted_Header_Checksum()
    {
        var png = TestFiles.CreatePng(10, 10);
        png[17] ^= 0xFF; // Flip a bit inside the IHDR width field without fixing the CRC.

        var result = Inspect(png);

        Assert.False(result.IsValid);
        Assert.Contains("checksum", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_Png_With_Zero_Width_Even_When_Checksum_Is_Valid()
    {
        var png = TestFiles.CreatePng(10, 10);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), TestFiles.Crc32(png.AsSpan(12, 17)));

        Assert.False(Inspect(png).IsValid);
    }

    [Fact]
    public void Requires_A_Seekable_Stream()
    {
        using var stream = new NonSeekableStream(TestFiles.Read("sample.png"));
        Assert.Throws<ArgumentException>(() => ImageFormatInspector.Inspect(stream));
    }

    private static ImageInspectionResult Inspect(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return ImageFormatInspector.Inspect(stream);
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
