using System.Buffers.Binary;

namespace ZeroZero.Tray.Tests;

/// <summary>
/// The bytes of a PNG as far as the container reads them: the signature, the header chunk with a
/// width and a height, and then whatever padding a test wants, so two frames of one size can still
/// differ in length and content. Nothing decodes these; the container copies them.
/// </summary>
internal static class PngFixture
{
    public static byte[] Bytes(uint width, uint height, int padding, byte fill = 0xAB)
    {
        var png = new byte[8 + 4 + 4 + 13 + 4 + padding];
        var span = png.AsSpan();
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(span);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 13);
        "IHDR"u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], width);
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], height);
        span[24] = 8;   // bit depth
        span[25] = 6;   // colour type: truecolour with alpha
        // Compression, filter, interlace stay zero; the CRC is not checked by the container.
        span[33..].Fill(fill);
        return png;
    }
}
