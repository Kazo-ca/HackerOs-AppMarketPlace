using System.Buffers.Binary;

namespace HackerOs.MarketPlace;

/// <summary>
/// Reads the size of a PNG image from its header, without decoding it: the signature, then the
/// first chunk, which the PNG specification requires to be <c>IHDR</c> (width and height first).
/// </summary>
public static class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Gets the size of a PNG image.</summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="width">Width in pixels when the file is a PNG.</param>
    /// <param name="height">Height in pixels when the file is a PNG.</param>
    /// <returns>Whether the bytes start like a PNG image with a valid <c>IHDR</c> chunk.</returns>
    public static bool TryReadSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        // 8 signature bytes, 4 length, 4 type "IHDR", 4 width, 4 height.
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(Signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (rawWidth is 0 or > int.MaxValue || rawHeight is 0 or > int.MaxValue)
        {
            return false;
        }

        width = (int)rawWidth;
        height = (int)rawHeight;
        return true;
    }
}
