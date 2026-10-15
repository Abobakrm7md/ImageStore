using System.Buffers.Binary;

namespace ImageStore.Application.Images.Validation;

/// <summary>Result of inspecting file content.</summary>
public sealed record ImageInspectionResult(ImageFormat? Format, int Width, int Height, string? Error)
{
    public bool IsValid => Error is null && Format is not null;

    public static ImageInspectionResult Valid(ImageFormat format, int width, int height) => new(format, width, height, null);

    public static ImageInspectionResult Invalid(string error) => new(null, 0, 0, error);
}

/// <summary>
/// Identifies JPEG, PNG and WebP files from their binary signatures and validates the header
/// structures needed to read the pixel dimensions. Client-supplied file names and Content-Type
/// headers are never consulted. Only headers are parsed – pixel data is not decoded – so the
/// cost is independent of the image size.
/// </summary>
public static class ImageFormatInspector
{
    private const int MaxJpegSegments = 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageInspectionResult Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[12];
        if (!TryReadExactly(stream, header))
        {
            return ImageInspectionResult.Invalid("The file is too small to be a valid image.");
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return InspectJpeg(stream);
        }

        if (header.StartsWith(PngSignature))
        {
            return InspectPng(stream);
        }

        if (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return InspectWebP(stream, BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]));
        }

        return ImageInspectionResult.Invalid("The file content is not a supported image format (JPEG, PNG or WebP).");
    }

    private static ImageInspectionResult InspectJpeg(Stream stream)
    {
        const string malformed = "The JPEG file is malformed or truncated.";
        Span<byte> buffer = stackalloc byte[5];
        stream.Position = 2; // Skip SOI (FF D8).

        for (var segment = 0; segment < MaxJpegSegments; segment++)
        {
            if (stream.ReadByte() != 0xFF)
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            int marker;
            do
            {
                marker = stream.ReadByte(); // 0xFF fill bytes may precede a marker.
            }
            while (marker == 0xFF);

            switch (marker)
            {
                case < 0: // End of stream.
                case 0x00: // Byte stuffing is only valid inside entropy-coded data.
                case 0xD8: // A second SOI.
                case 0xD9: // EOI before any frame header.
                case 0xDA: // Scan data before any frame header.
                    return ImageInspectionResult.Invalid(malformed);
                case 0x01:
                case >= 0xD0 and <= 0xD7:
                    continue; // Stand-alone markers without a length field.
            }

            if (!TryReadExactly(stream, buffer[..2]))
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            var segmentEnd = stream.Position + length - 2;
            if (length < 2 || segmentEnd > stream.Length)
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            if (IsStartOfFrame(marker))
            {
                if (length < 8 || !TryReadExactly(stream, buffer))
                {
                    return ImageInspectionResult.Invalid(malformed);
                }

                // buffer[0] is the sample precision; height and width follow (big-endian).
                var height = BinaryPrimitives.ReadUInt16BigEndian(buffer[1..3]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(buffer[3..5]);
                return width == 0 || height == 0
                    ? ImageInspectionResult.Invalid("The JPEG image dimensions are invalid.")
                    : ImageInspectionResult.Valid(ImageFormat.Jpeg, width, height);
            }

            stream.Position = segmentEnd;
        }

        return ImageInspectionResult.Invalid(malformed);
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    private static ImageInspectionResult InspectPng(Stream stream)
    {
        // The first chunk must be IHDR: length(4) "IHDR"(4) data(13) crc(4).
        Span<byte> chunk = stackalloc byte[25];
        stream.Position = 8;
        if (!TryReadExactly(stream, chunk))
        {
            return ImageInspectionResult.Invalid("The PNG file is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(chunk) != 13 || !chunk[4..8].SequenceEqual("IHDR"u8))
        {
            return ImageInspectionResult.Invalid("The PNG file does not start with a valid header chunk.");
        }

        var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(chunk[21..25]);
        if (Crc32.Compute(chunk[4..21]) != expectedCrc)
        {
            return ImageInspectionResult.Invalid("The PNG header checksum is invalid.");
        }

        var data = chunk[8..21];
        var width = BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        byte bitDepth = data[8], colorType = data[9], compression = data[10], filter = data[11], interlace = data[12];

        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            return ImageInspectionResult.Invalid("The PNG image dimensions are invalid.");
        }

        if (!IsValidPngColorDepth(colorType, bitDepth) || compression != 0 || filter != 0 || interlace > 1)
        {
            return ImageInspectionResult.Invalid("The PNG header contains unsupported values.");
        }

        return ImageInspectionResult.Valid(ImageFormat.Png, (int)width, (int)height);
    }

    private static bool IsValidPngColorDepth(byte colorType, byte bitDepth) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 or 4 or 6 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        _ => false,
    };

    private static ImageInspectionResult InspectWebP(Stream stream, uint riffSize)
    {
        const string malformed = "The WebP file is malformed or truncated.";

        // The RIFF size covers everything after the first 8 bytes and must fit in the file.
        if (riffSize < 12 || riffSize + 8L > stream.Length)
        {
            return ImageInspectionResult.Invalid(malformed);
        }

        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> data = stackalloc byte[10];
        stream.Position = 12;
        if (!TryReadExactly(stream, chunkHeader))
        {
            return ImageInspectionResult.Invalid(malformed);
        }

        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
        if (chunkSize > riffSize - 12)
        {
            return ImageInspectionResult.Invalid(malformed);
        }

        int width, height;
        var fourCc = chunkHeader[..4];
        if (fourCc.SequenceEqual("VP8 "u8))
        {
            // Lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width and height.
            if (chunkSize < 10 || !TryReadExactly(stream, data))
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            var isKeyFrame = (data[0] & 0x01) == 0;
            if (!isKeyFrame || data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]) & 0x3FFF;
        }
        else if (fourCc.SequenceEqual("VP8L"u8))
        {
            // Lossless: signature 0x2F, then 14-bit (width-1), 14-bit (height-1), alpha bit, 3-bit version.
            if (chunkSize < 5 || !TryReadExactly(stream, data[..5]) || data[0] != 0x2F)
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..5]);
            if (bits >> 29 != 0)
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
        }
        else if (fourCc.SequenceEqual("VP8X"u8))
        {
            // Extended: flags(1) reserved(3) canvas width-1 (24-bit LE) canvas height-1 (24-bit LE).
            if (chunkSize < 10 || !TryReadExactly(stream, data))
            {
                return ImageInspectionResult.Invalid(malformed);
            }

            width = 1 + (data[4] | (data[5] << 8) | (data[6] << 16));
            height = 1 + (data[7] | (data[8] << 8) | (data[9] << 16));
        }
        else
        {
            return ImageInspectionResult.Invalid(malformed);
        }

        return width == 0 || height == 0
            ? ImageInspectionResult.Invalid("The WebP image dimensions are invalid.")
            : ImageInspectionResult.Valid(ImageFormat.WebP, width, height);
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    /// <summary>CRC-32 (ISO-HDLC) as used by PNG chunk checksums.</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
            {
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (var n = 0u; n < table.Length; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
