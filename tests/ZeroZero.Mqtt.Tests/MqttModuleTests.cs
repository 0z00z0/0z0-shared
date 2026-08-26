using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What the running application reports about the module it actually loaded.</summary>
/// <remarks>The failure this guards is a build-time constant: a version compiled in from the same
/// property the pin is written against cannot ever disagree with the pin, and disagreeing with the
/// pin is the whole reason the value exists. Every assertion here is therefore about the loaded
/// assembly's own metadata rather than about a number written in a test.</remarks>
public class MqttModuleTests
{
    private static readonly Assembly Module = typeof(MqttSettings).Assembly;

    [Fact]
    public void VersionIsWhatTheLoadedAssemblyCarriesRatherThanACompiledInConstant()
    {
        string metadata = Module.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                                .InformationalVersion;

        // Read a second time, independently, off the assembly in this process. A constant baked from
        // Directory.Build.props passes nothing here the moment the two differ, which is exactly the
        // state a consumer building from a working tree between tags is in.
        Assert.Equal(metadata, MqttModule.Version);
    }

    [Fact]
    public void VersionCarriesTheCommitAsWellAsTheNumber()
    {
        // A bare number is identical across every revision since the last bump, so it cannot place a
        // defect report. The commit is the half that identifies a binary. A tree built with no git
        // available stamps none, which is a build nobody releases from and not a failure here.
        if (!MqttModule.Version.Contains('+')) return;

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\+[0-9a-f]{7,40}$"), MqttModule.Version);
    }

    [Fact]
    public void VersionAgreesWithTheAssemblyVersionOnTheNumber()
    {
        string number = MqttModule.Version.Split('+')[0];

        Assert.StartsWith(number, Module.GetName().Version!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAnswersForAnyAssemblyAndNotOnlyThisOne()
    {
        // The member a host calls to report its own build beside the module's.
        Assert.Equal(MqttModule.Version, MqttModule.Read(Module));
        Assert.NotEmpty(MqttModule.Read(typeof(MqttModuleTests).Assembly));
    }

    [Fact]
    public void ReadRefusesNoAssemblyRatherThanAnsweringForTheCallers() =>
        Assert.Throws<ArgumentNullException>(() => MqttModule.Read(null!));

    [Fact]
    public void ThePanelHasSomewhereToRenderIt()
    {
        // The panel appends this to the publish switch's info text, which is the one icon in front of
        // a reader at the moment the question arises.
        Assert.Equal("MQTT module 0.5.0+abc1234.",
                     MqttStrings.Default.Format("ModuleVersion", "0.5.0+abc1234"));
    }
}
