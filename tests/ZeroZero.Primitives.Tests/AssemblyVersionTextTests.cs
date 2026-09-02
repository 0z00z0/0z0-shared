using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Primitives.Tests;

/// <summary>What an assembly reports about itself, and the form an About box shows.</summary>
/// <remarks>The failure the reader guards is a build-time constant: a version compiled in from the
/// same property the pin is written against cannot ever disagree with the pin, and disagreeing with
/// the pin is the whole reason the value exists. The assertions about the loaded assembly are
/// therefore about its own metadata rather than about a number written in a test; the fallback
/// cases use assemblies built in the test with exactly the metadata the case needs.</remarks>
public class AssemblyVersionTextTests
{
    private static readonly Assembly Primitives = typeof(AssemblyVersionText).Assembly;

    /// <summary>An assembly carrying exactly the metadata named: a four-part version, an
    /// informational version, or both.</summary>
    private static Assembly Built(Version version, string? informational)
    {
        var name = new AssemblyName($"Built.{Guid.NewGuid():N}") { Version = version };
        var builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        if (informational is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));
        }
        return builder;
    }

    /// <summary>An assembly with no version of either kind. A dynamic assembly always answers
    /// 0.0.0.0 for its assembly version, so the case with nothing at all needs a hand-written
    /// one.</summary>
    private sealed class Unversioned : Assembly
    {
        public override AssemblyName GetName() => new("Unversioned");

        // Typed Attribute[] at runtime: the framework casts the answer to that before reading it.
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<Attribute>();

        public override object[] GetCustomAttributes(bool inherit) => Array.Empty<Attribute>();

        public override bool IsDefined(Type attributeType, bool inherit) => false;

        public override IList<CustomAttributeData> GetCustomAttributesData() => [];
    }

    [Fact]
    public void ReadIsWhatTheLoadedAssemblyCarriesRatherThanACompiledInConstant()
    {
        string metadata = Primitives.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                                    .InformationalVersion;

        Assert.Equal(metadata, AssemblyVersionText.Read(Primitives));
    }

    [Fact]
    public void ReadCarriesTheCommitAsWellAsTheNumber()
    {
        // A tree built with no git available stamps none, which is a build nobody releases from and
        // not a failure here.
        string version = AssemblyVersionText.Read(Primitives);
        if (!version.Contains('+')) return;

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\+[0-9a-f]{7,40}$"), version);
    }

    [Fact]
    public void ReadPrefersTheInformationalVersionBecauseOnlyItCarriesTheCommit() =>
        Assert.Equal("1.2.3+abc1234", AssemblyVersionText.Read(Built(new Version(1, 2, 3, 0), "1.2.3+abc1234")));

    [Fact]
    public void ReadFallsBackToTheAssemblyVersionWhenNothingInformationalIsStamped() =>
        Assert.Equal("1.2.3.4", AssemblyVersionText.Read(Built(new Version(1, 2, 3, 4), null)));

    [Fact]
    public void ReadTreatsAnEmptyInformationalVersionAsAbsent() =>
        Assert.Equal("1.2.3.4", AssemblyVersionText.Read(Built(new Version(1, 2, 3, 4), "")));

    [Fact]
    public void ReadReportsNothingRatherThanAFabricatedNumber() =>
        Assert.Equal("", AssemblyVersionText.Read(new Unversioned()));

    [Fact]
    public void ReadRefusesNoAssemblyRatherThanAnsweringForTheCaller() =>
        Assert.Throws<ArgumentNullException>(() => AssemblyVersionText.Read(null!));

    [Theory]
    [InlineData("0.7.0+0123456789abcdef0123456789abcdef01234567", "0.7.0+0123456")]
    [InlineData("1.28.2-beta.1+0123456789ABCDEF0123456789ABCDEF01234567", "1.28.2-beta.1+0123456")]
    [InlineData("0.7.0+fc0ad0b", "0.7.0+fc0ad0b")]
    [InlineData("0.7.0+abc", "0.7.0+abc")]
    [InlineData("0.7.0", "0.7.0")]
    [InlineData("0.7.0+build.20260902", "0.7.0+build.20260902")]
    [InlineData("", "")]
    public void ForDisplayShortensACommitAndOnlyACommit(string version, string display) =>
        Assert.Equal(display, AssemblyVersionText.ForDisplay(version));

    [Fact]
    public void ForDisplayRefusesNothing() =>
        Assert.Throws<ArgumentNullException>(() => AssemblyVersionText.ForDisplay(null!));
}
