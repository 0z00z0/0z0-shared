using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Primitives.Tests;

/// <summary>The relaunch and watchdog limiters keep their counts in this file, so a moment written
/// must read back exactly and a damaged line must cost that line alone.</summary>
public sealed class TimestampListFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Primitives.Tests." + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "nested", "stamps.txt");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void WrittenMomentsReadBackExactly_AndALineThatDoesNotParseIsDropped()
    {
        DateTimeOffset[] written =
        [
            new DateTimeOffset(2026, 10, 11, 14, 3, 5, TimeSpan.Zero).AddTicks(1234567),
            new(2026, 10, 11, 16, 3, 5, TimeSpan.FromHours(2)),
        ];
        TimestampListFile.Write(FilePath, written);
        File.AppendAllLines(FilePath, ["not a moment", ""]);

        Assert.Equal(written, TimestampListFile.Read(FilePath));
    }

    [Fact]
    public void AMissingFileReadsAsEmpty() => Assert.Empty(TimestampListFile.Read(FilePath));
}
