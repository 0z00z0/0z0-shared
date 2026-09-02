using System.Buffers.Binary;
using Xunit;

namespace ZeroZero.Tray.Tests;

public class IcoFileTests
{
    // Three frames that differ on every axis the directory records: width against height (no
    // square), 256 on one side each way (the value the format spells as zero), and three lengths.
    private static readonly byte[] Wide = PngFixture.Bytes(20, 12, padding: 7, fill: 0x11);
    private static readonly byte[] Widest = PngFixture.Bytes(256, 40, padding: 3, fill: 0x22);
    private static readonly byte[] Tallest = PngFixture.Bytes(33, 256, padding: 11, fill: 0x33);

    [Fact]
    public void Build_WritesTheDirectoryFromEachFramesOwnHeader()
    {
        byte[] file = IcoFile.Build([Wide, Widest, Tallest]);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)));

        AssertEntry(file, 0, width: 20, height: 12, length: Wide.Length, offset: 6 + 48);
        AssertEntry(file, 1, width: 0, height: 40, length: Widest.Length, offset: 6 + 48 + Wide.Length);
        AssertEntry(file, 2, width: 33, height: 0, length: Tallest.Length, offset: 6 + 48 + Wide.Length + Widest.Length);
    }

    [Fact]
    public void Build_CopiesEveryFrameIntactAtItsOffset()
    {
        byte[] file = IcoFile.Build([Wide, Widest, Tallest]);

        Assert.Equal(6 + 48 + Wide.Length + Widest.Length + Tallest.Length, file.Length);
        Assert.Equal(Wide, file.AsSpan(6 + 48, Wide.Length).ToArray());
        Assert.Equal(Widest, file.AsSpan(6 + 48 + Wide.Length, Widest.Length).ToArray());
        Assert.Equal(Tallest, file.AsSpan(6 + 48 + Wide.Length + Widest.Length, Tallest.Length).ToArray());
    }

    [Fact]
    public void Build_KeepsTheOrderGiven()
    {
        byte[] file = IcoFile.Build([Tallest, Wide]);

        AssertEntry(file, 0, width: 33, height: 0, length: Tallest.Length, offset: 6 + 32);
        AssertEntry(file, 1, width: 20, height: 12, length: Wide.Length, offset: 6 + 32 + Tallest.Length);
    }

    [Fact]
    public void Write_WritesWhatBuildReturns()
    {
        using var stream = new MemoryStream();
        IcoFile.Write(stream, [Wide, Widest]);

        Assert.Equal(IcoFile.Build([Wide, Widest]), stream.ToArray());
    }

    [Fact]
    public void Build_RefusesNoFrames()
    {
        var ex = Assert.Throws<ArgumentException>(() => IcoFile.Build([]));
        Assert.Contains("at least one frame", ex.Message);
    }

    [Fact]
    public void Build_RefusesAFrameThatIsNotAPng()
    {
        byte[] bitmap = [0x42, 0x4D, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var ex = Assert.Throws<ArgumentException>(() => IcoFile.Build([Wide, bitmap]));
        Assert.Contains("not a PNG", ex.Message);
    }

    [Fact]
    public void Build_RefusesTwoFramesOfOneSizeWhateverTheirContent()
    {
        byte[] first = PngFixture.Bytes(24, 24, padding: 5, fill: 0x01);
        byte[] second = PngFixture.Bytes(24, 24, padding: 9, fill: 0x02);

        var ex = Assert.Throws<ArgumentException>(() => IcoFile.Build([first, second]));
        Assert.Contains("24x24", ex.Message);
    }

    [Theory]
    [InlineData(257u, 10u)]
    [InlineData(10u, 300u)]
    [InlineData(0u, 16u)]
    [InlineData(16u, 0u)]
    public void ReadPngSize_RefusesASideAnIconCannotHold(uint width, uint height)
    {
        Assert.Throws<ArgumentException>(() => IcoFile.ReadPngSize(PngFixture.Bytes(width, height, padding: 0)));
    }

    [Fact]
    public void ReadPngSize_KeepsWidthAndHeightApart()
    {
        Assert.Equal((20, 12), IcoFile.ReadPngSize(Wide));
        Assert.Equal((256, 40), IcoFile.ReadPngSize(Widest));
        Assert.Equal((33, 256), IcoFile.ReadPngSize(Tallest));
    }

    [Fact]
    public void ReadPngSize_RefusesAPngWhoseFirstChunkIsNotTheHeader()
    {
        byte[] png = PngFixture.Bytes(16, 16, padding: 0);
        "IDAT"u8.CopyTo(png.AsSpan(12));

        var ex = Assert.Throws<ArgumentException>(() => IcoFile.ReadPngSize(png));
        Assert.Contains("header chunk", ex.Message);
    }

    [Fact]
    public void ReadPngSize_RefusesBytesShorterThanAHeader()
    {
        Assert.Throws<ArgumentException>(() => IcoFile.ReadPngSize(PngFixture.Bytes(16, 16, padding: 0).AsSpan(0, 20)));
    }

    private static void AssertEntry(byte[] file, int index, int width, int height, int length, int offset)
    {
        var entry = file.AsSpan(6 + 16 * index, 16);
        Assert.Equal(width, entry[0]);
        Assert.Equal(height, entry[1]);
        Assert.Equal(0, entry[2]);
        Assert.Equal(0, entry[3]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
        Assert.Equal((uint)length, BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
        Assert.Equal((uint)offset, BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
    }
}
