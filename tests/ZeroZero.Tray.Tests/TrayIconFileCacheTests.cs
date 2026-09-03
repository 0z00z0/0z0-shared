using Xunit;
using ZeroZero.Tray.WinUI;

namespace ZeroZero.Tray.Tests;

public sealed class TrayIconFileCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Tray.Tests." + Guid.NewGuid().ToString("N"));

    private static readonly byte[] Small = PngFixture.Bytes(16, 16, padding: 5, fill: 0x11);
    private static readonly byte[] Large = PngFixture.Bytes(32, 32, padding: 9, fill: 0x22);
    private static readonly byte[] SmallOther = PngFixture.Bytes(16, 16, padding: 5, fill: 0x33);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Resolve_WritesTheComposedFileOnAFirstRender()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");

        var (path, changed) = cache.Resolve(TrayIconImage.FromFrames([Small, Large]));

        Assert.True(changed);
        Assert.Equal(cache.Path, path);
        Assert.Equal(IcoFile.Build([Small, Large]), File.ReadAllBytes(path));
        Assert.Equal(1, cache.Writes);
    }

    [Fact]
    public void Resolve_SkipsTheWriteWhenTheRenderRepeatsTheLast()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");
        cache.Resolve(TrayIconImage.FromFrames([Small, Large]));
        DateTime written = File.GetLastWriteTimeUtc(cache.Path);

        var (path, changed) = cache.Resolve(TrayIconImage.FromFrames([Small, Large]));

        Assert.False(changed);
        Assert.Equal(cache.Path, path);
        Assert.Equal(1, cache.Writes);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Resolve_WritesAgainWhenARenderDiffers()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");
        cache.Resolve(TrayIconImage.FromFrames([Small, Large]));

        var (_, changed) = cache.Resolve(TrayIconImage.FromFrames([SmallOther, Large]));

        Assert.True(changed);
        Assert.Equal(2, cache.Writes);
        Assert.Equal(IcoFile.Build([SmallOther, Large]), File.ReadAllBytes(cache.Path));
    }

    [Fact]
    public void Resolve_WritesAgainWhenTheFileWentMissing()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");
        cache.Resolve(TrayIconImage.FromFrames([Small]));
        File.Delete(cache.Path);

        var (_, changed) = cache.Resolve(TrayIconImage.FromFrames([Small]));

        Assert.True(changed);
        Assert.True(File.Exists(cache.Path));
    }

    [Fact]
    public void Resolve_PassesAnApplicationOwnedFileThroughUntouched()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");
        string own = Path.Combine(_dir, "own.ico");

        var (path, changed) = cache.Resolve(TrayIconImage.FromFile(own));

        Assert.True(changed);
        Assert.Equal(own, path);
        Assert.Equal(0, cache.Writes);
        Assert.False(File.Exists(cache.Path));
    }

    [Fact]
    public void Resolve_ForgetsTheLastRenderAcrossAnApplicationOwnedFile()
    {
        var cache = new TrayIconFileCache(_dir, "Harness");
        cache.Resolve(TrayIconImage.FromFrames([Small]));
        cache.Resolve(TrayIconImage.FromFile(Path.Combine(_dir, "own.ico")));

        var (_, changed) = cache.Resolve(TrayIconImage.FromFrames([Small]));

        Assert.True(changed);
        Assert.Equal(2, cache.Writes);
    }

    [Fact]
    public void ThePathIsDerivedFromTheNameWithUnsafeCharactersReplaced()
    {
        var cache = new TrayIconFileCache(_dir, "Charge Keeper: tray");

        Assert.Equal(Path.Combine(_dir, "Charge-Keeper--tray.ico"), cache.Path);
    }

    [Fact]
    public void FromFrames_RefusesAnEmptyList()
    {
        Assert.Throws<ArgumentException>(() => TrayIconImage.FromFrames([]));
    }
}
