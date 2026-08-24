using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>The two data contracts an About surface is handed: <see cref="AboutInfo"/> and the
/// <see cref="ExternalLibrary"/> credits it carries.</summary>
public class AboutInfoTests
{
    private static AboutInfo Sample() => new()
    {
        AppName     = "ChargeKeeper",
        Version     = "1.4.2",
        Description = "Keeps a laptop battery inside its healthy charge band.",
        RepoUrl     = "https://github.com/0z00z0/chargekeeper",
    };

    [Fact]
    public void ExternalLibraries_DefaultsToEmptyRatherThanNull()
        => Assert.Empty(Sample().ExternalLibraries);

    [Fact]
    public void RequiredMembers_RoundTripUnchanged()
    {
        var info = Sample();

        Assert.Equal("ChargeKeeper", info.AppName);
        Assert.Equal("1.4.2", info.Version);
        Assert.Equal("https://github.com/0z00z0/chargekeeper", info.RepoUrl);
    }

    [Fact]
    public void Equality_IsByValue()
        => Assert.Equal(Sample(), Sample());

    [Fact]
    public void Equality_DistinguishesADifferentVersion()
        => Assert.NotEqual(Sample(), Sample() with { Version = "1.4.3" });

    [Fact]
    public void With_LeavesTheOriginalUntouched()
    {
        var original = Sample();
        var renamed  = original with { AppName = "HyperVManagerTray" };

        Assert.Equal("HyperVManagerTray", renamed.AppName);
        Assert.Equal("ChargeKeeper", original.AppName);
        Assert.Equal(original.Version, renamed.Version);
    }

    [Fact]
    public void ExternalLibraries_KeepTheOrderTheyWereSuppliedIn()
    {
        var info = Sample() with
        {
            ExternalLibraries =
            [
                new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause"),
                new ExternalLibrary("xunit", "xunit contributors", "Unit testing", "Apache-2.0"),
            ],
        };

        Assert.Collection(
            info.ExternalLibraries,
            first  => Assert.Equal("NLog", first.Name),
            second => Assert.Equal("xunit", second.Name));
    }

    [Fact]
    public void ExternalLibrary_UrlIsOptionalAndDefaultsToNull()
        => Assert.Null(new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause").Url);

    [Fact]
    public void ExternalLibrary_ExposesEveryPositionalMember()
    {
        var lib = new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause", "https://nlog-project.org");

        Assert.Equal("NLog", lib.Name);
        Assert.Equal("NLog contributors", lib.Author);
        Assert.Equal("File logging", lib.Purpose);
        Assert.Equal("BSD-3-Clause", lib.License);
        Assert.Equal("https://nlog-project.org", lib.Url);
    }

    [Fact]
    public void ExternalLibrary_EqualityIsByValue()
    {
        var a = new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause");
        var b = new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause");

        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { License = "MIT" });
    }
}
