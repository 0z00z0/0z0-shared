using System.Reflection;
using Xunit;
using ZeroZero.Diagnostics;
using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>The first line of a run. The version half is the primitives reader's and is tested
/// there; what is asserted here is the line built around it, and that the commit reaches the log
/// whole rather than in the About-box form.</summary>
public class StartupVersionLineTests
{
    private const string FortyCharacterCommit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly Assembly Diagnostics = typeof(StartupVersionLine).Assembly;

    [Fact]
    public void TheLineIsTheNameThenTheVersionTheAssemblyCarriesThenStarting()
    {
        string version = AssemblyVersionText.Read(Diagnostics);
        Assert.NotEqual("", version);

        Assert.Equal($"ZeroZero.Diagnostics {version} starting", StartupVersionLine.For(Diagnostics));
    }

    [Fact]
    public void TheCommitReachesTheLineWholeRatherThanInTheAboutBoxForm()
    {
        Assembly built = TestAssemblies.Built(new Version(1, 2, 3, 0), "1.2.3+" + FortyCharacterCommit, "Stamped");

        Assert.Equal("Stamped 1.2.3+" + FortyCharacterCommit + " starting", StartupVersionLine.For(built));
    }

    [Fact]
    public void AnAssemblyWithNoVersionGivesTheNameAlone() =>
        Assert.Equal("Unversioned starting", StartupVersionLine.For(new UnversionedAssembly()));

    [Fact]
    public void WriteSendsTheLineAsInfoAndNothingElse()
    {
        var sink = new RecordingSink();

        StartupVersionLine.Write(sink, Diagnostics);

        Assert.Equal(("info", StartupVersionLine.For(Diagnostics), null), Assert.Single(sink.Entries));
    }

    [Fact]
    public void BothRefuseNothing()
    {
        Assert.Throws<ArgumentNullException>(() => StartupVersionLine.For(null!));
        Assert.Throws<ArgumentNullException>(() => StartupVersionLine.Write(null!, Diagnostics));
        Assert.Throws<ArgumentNullException>(() => StartupVersionLine.Write(new RecordingSink(), null!));
    }
}
