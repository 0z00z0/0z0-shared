using Xunit;
using ZeroZero.Diagnostics.Dumps;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>Every value is the application's, and a value Windows Error Reporting could not act on
/// is refused where it is written rather than discovered at the next crash.</summary>
public class DumpPolicyTests
{
    [Fact]
    public void AFullPathIsAccepted()
    {
        var policy = new DumpPolicy("App.exe", @"C:\ProgramData\App\dumps", DumpType.Full, 2);

        Assert.Equal("App.exe", policy.ExecutableName);
        Assert.Equal(@"C:\ProgramData\App\dumps", policy.DumpDirectory);
        Assert.Equal(DumpType.Full, policy.DumpType);
        Assert.Equal(2, policy.RetainedCount);
    }

    [Fact]
    public void AnEnvironmentVariablePathIsAcceptedBecauseWindowsErrorReportingExpandsIt() =>
        Assert.Equal(@"%LOCALAPPDATA%\App\dumps", new DumpPolicy("App.exe", @"%LOCALAPPDATA%\App\dumps", DumpType.Mini, 1).DumpDirectory);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"C:\Program Files\App\App.exe")]
    [InlineData("bin/App.exe")]
    public void AnExecutableNameThatIsNotABareFileNameIsRefused(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new DumpPolicy(name!, @"C:\dumps", DumpType.Mini, 1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dumps")]
    [InlineData(@"..\dumps")]
    public void ADumpDirectoryThatIsNeitherFullNorAVariableIsRefused(string? directory) =>
        Assert.ThrowsAny<ArgumentException>(() => new DumpPolicy("App.exe", directory!, DumpType.Mini, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void ADumpTypeWindowsErrorReportingDoesNotDefineIsRefused(int type) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DumpPolicy("App.exe", @"C:\dumps", (DumpType)type, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARetainedCountBelowOneIsRefused(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DumpPolicy("App.exe", @"C:\dumps", DumpType.Mini, count));

    [Fact]
    public void TheEnumValuesAreWindowsErrorReportingsOwn()
    {
        Assert.Equal(1, (int)DumpType.Mini);
        Assert.Equal(2, (int)DumpType.Full);
    }
}
