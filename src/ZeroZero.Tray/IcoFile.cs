using System.Buffers.Binary;

namespace ZeroZero.Tray;

/// <summary>
/// Writes a multi-size icon file whose frames are PNG streams: the form the shell reads, and the
/// one a notify-icon library reloads from disk after the taskbar is recreated, which is why an
/// application writes its icon to a file rather than handing over a bitmap handle that leaks.
/// </summary>
/// <remarks>
/// Each frame's directory entry is filled from the PNG's own header, never from a size the
/// caller claims, so an entry can never disagree with the image behind it. A frame that is not a
/// PNG, larger than 256 on either side, or the same size as another is refused.
/// </remarks>
public static class IcoFile
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;
    private const int LargestSide = 256;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The whole file for the given PNG frames, in the order given.</summary>
    public static byte[] Build(IReadOnlyList<byte[]> pngFrames)
    {
        ArgumentNullException.ThrowIfNull(pngFrames);
        if (pngFrames.Count == 0) throw new ArgumentException("An icon needs at least one frame.", nameof(pngFrames));

        var sizes = new (int Width, int Height)[pngFrames.Count];
        for (int i = 0; i < pngFrames.Count; i++)
        {
            sizes[i] = ReadPngSize(pngFrames[i]);
            for (int j = 0; j < i; j++)
                if (sizes[j] == sizes[i])
                    throw new ArgumentException($"Frames {j} and {i} are both {sizes[i].Width}x{sizes[i].Height}; the shell takes the first and the second is dead weight.", nameof(pngFrames));
        }

        int total = HeaderSize + EntrySize * pngFrames.Count;
        foreach (var frame in pngFrames) total += frame.Length;

        var file = new byte[total];
        var span = file.AsSpan();
        // ICONDIR: reserved, type 1 for an icon, count.
        BinaryPrimitives.WriteUInt16LittleEndian(span, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)pngFrames.Count);

        int offset = HeaderSize + EntrySize * pngFrames.Count;
        for (int i = 0; i < pngFrames.Count; i++)
        {
            var entry = span.Slice(HeaderSize + EntrySize * i, EntrySize);
            // A side is one byte, so 256 is written as 0: the one value the format cannot spell.
            entry[0] = (byte)(sizes[i].Width == LargestSide ? 0 : sizes[i].Width);
            entry[1] = (byte)(sizes[i].Height == LargestSide ? 0 : sizes[i].Height);
            entry[2] = 0;   // colour count: none, the frame is true colour
            entry[3] = 0;   // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);    // planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);   // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)pngFrames[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);

            pngFrames[i].CopyTo(span[offset..]);
            offset += pngFrames[i].Length;
        }

        return file;
    }

    /// <summary>Writes the file for the given PNG frames to <paramref name="stream"/>.</summary>
    public static void Write(Stream stream, IReadOnlyList<byte[]> pngFrames)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(Build(pngFrames));
    }

    /// <summary>
    /// The size a PNG declares in its header. Throws when the bytes are not a PNG, or when a side
    /// is zero or more than 256, the most an icon frame may be.
    /// </summary>
    public static (int Width, int Height) ReadPngSize(ReadOnlySpan<byte> png)
    {
        // Signature, then the IHDR chunk: length, "IHDR", width, height — 24 bytes in all.
        if (png.Length < 24 || !png[..8].SequenceEqual(PngSignature))
            throw new ArgumentException("The frame is not a PNG.", nameof(png));
        if (!png.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new ArgumentException("The PNG does not start with its header chunk.", nameof(png));

        uint width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        if (width is 0 or > LargestSide || height is 0 or > LargestSide)
            throw new ArgumentException($"A frame of {width}x{height} does not fit an icon; each side is 1 to 256.", nameof(png));

        return ((int)width, (int)height);
    }
}
