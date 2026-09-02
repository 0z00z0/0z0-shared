using Microsoft.Win32;
using Xunit;
using ZeroZero.Diagnostics.Dumps;
using ZeroZero.Diagnostics.Tests;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>The lifecycle, against a real registry under a scratch key. What Windows Error Reporting
/// would read is asserted by opening the keys directly, not through the class under test.</summary>
public class DumpRegistrationTests : IDisposable
{
    private readonly ScratchHive _hive = new();
    private readonly RecordingSink _log = new();
    private readonly DumpRegistration _registration;

    private static readonly DumpPolicy App = new("App.exe", @"%LOCALAPPDATA%\App\dumps", DumpType.Full, 2);

    public DumpRegistrationTests() => _registration = new DumpRegistration(_hive.Root, ScratchHive.LocalDumps, _log);

    public void Dispose() => _hive.Dispose();

    [Fact]
    public void ArmWritesTheThreeValuesWindowsErrorReportingReadsInTheKindsItExpects()
    {
        _registration.Arm(App);

        using RegistryKey? key = _hive.Open("App.exe");
        Assert.NotNull(key);
        Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("DumpFolder"));
        Assert.Equal(@"%LOCALAPPDATA%\App\dumps", key.GetValue("DumpFolder", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("DumpCount"));
        Assert.Equal(2, key.GetValue("DumpCount"));
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("DumpType"));
        Assert.Equal(2, key.GetValue("DumpType"));
        Assert.Equal(3, key.ValueCount);
    }

    [Fact]
    public void ArmSaysWhatItArmedInTheLog()
    {
        _registration.Arm(App);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal("info", entry.Kind);
        Assert.Contains("App.exe", entry.Text);
        Assert.Contains("Full", entry.Text);
        Assert.Contains("2 retained", entry.Text);
    }

    [Fact]
    public void ReadGivesBackWhatArmWrote()
    {
        _registration.Arm(App);

        Assert.Equal(App, _registration.Read("App.exe"));
    }

    [Fact]
    public void ArmReplacesAnEarlierRegistration()
    {
        _registration.Arm(new DumpPolicy("App.exe", @"C:\old", DumpType.Mini, 10));

        _registration.Arm(App);

        Assert.Equal(App, _registration.Read("App.exe"));
    }

    [Fact]
    public void ReadIsNullWhereNothingIsRegisteredAndWhereTheRegistrationIsIncomplete()
    {
        Assert.Null(_registration.Read("App.exe"));

        using (RegistryKey partial = _hive.Root.CreateSubKey(ScratchHive.LocalDumps + @"\Partial.exe"))
            partial.SetValue("DumpFolder", @"C:\dumps", RegistryValueKind.ExpandString);

        Assert.True(_registration.IsArmed("Partial.exe"));
        Assert.Null(_registration.Read("Partial.exe"));
    }

    [Fact]
    public void DisarmRemovesTheRegistrationAndTheRootItLeavesEmpty()
    {
        _registration.Arm(App);
        Assert.True(_registration.IsArmed("App.exe"));

        _registration.Disarm("App.exe");

        Assert.False(_registration.IsArmed("App.exe"));
        Assert.Null(_hive.OpenLocalDumps());
        Assert.Contains(_log.Entries, entry => entry.Text.Contains("disarmed for App.exe"));
        Assert.Contains(_log.Entries, entry => entry.Text.Contains("empty local dumps root"));
    }

    [Fact]
    public void DisarmKeepsTheRootWhileAnotherRegistrationRemains()
    {
        _registration.Arm(App);
        _registration.Arm(new DumpPolicy("Other.exe", @"C:\other", DumpType.Mini, 1));

        _registration.Disarm("App.exe");

        Assert.False(_registration.IsArmed("App.exe"));
        Assert.True(_registration.IsArmed("Other.exe"));
        using RegistryKey? root = _hive.OpenLocalDumps();
        Assert.NotNull(root);
    }

    [Fact]
    public void DisarmKeepsTheRootWhileItHoldsAGlobalSetting()
    {
        using (RegistryKey root = _hive.Root.CreateSubKey(ScratchHive.LocalDumps))
            root.SetValue("DumpType", 1, RegistryValueKind.DWord);
        _registration.Arm(App);

        _registration.Disarm("App.exe");

        using RegistryKey? kept = _hive.OpenLocalDumps();
        Assert.NotNull(kept);
        Assert.Equal(1, kept.GetValue("DumpType"));
    }

    [Fact]
    public void DisarmOfNothingIsQuietAndCreatesNothing()
    {
        var exception = Record.Exception(() => _registration.Disarm("App.exe"));

        Assert.Null(exception);
        Assert.Null(_hive.OpenLocalDumps());
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void ApplyFollowsTheFlagTheApplicationHolds()
    {
        _registration.Apply(App, armed: true);
        Assert.True(_registration.IsArmed("App.exe"));

        _registration.Apply(App, armed: false);
        Assert.False(_registration.IsArmed("App.exe"));
    }

    [Fact]
    public void RemoveResidueRemovesTheNamedRegistrationsAndOnlyThose()
    {
        _registration.Arm(App);
        _registration.Arm(new DumpPolicy("OldName.exe", @"C:\old", DumpType.Mini, 1));
        _registration.Arm(new DumpPolicy("OlderName.exe", @"C:\older", DumpType.Mini, 1));

        int removed = _registration.RemoveResidue("OldName.exe", "OlderName.exe", "NeverThere.exe");

        Assert.Equal(2, removed);
        Assert.True(_registration.IsArmed("App.exe"));
        Assert.False(_registration.IsArmed("OldName.exe"));
        Assert.False(_registration.IsArmed("OlderName.exe"));
        Assert.Equal(2, _log.Entries.Count(entry => entry.Text.Contains("older build left")));
    }

    [Fact]
    public void RemoveResidueRemovesTheRootWhenItEmptiesIt()
    {
        _registration.Arm(new DumpPolicy("OldName.exe", @"C:\old", DumpType.Mini, 1));

        Assert.Equal(1, _registration.RemoveResidue(["OldName.exe"]));

        Assert.Null(_hive.OpenLocalDumps());
    }

    [Fact]
    public void RemoveRootIfEmptyLeavesAMissingRootAlone()
    {
        Assert.False(_registration.RemoveRootIfEmpty());
        Assert.Null(_hive.OpenLocalDumps());
    }

    [Fact]
    public void TheRealRootIsWhereWindowsErrorReportingReads() =>
        Assert.Equal(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps", DumpRegistration.LocalDumpsPath);

    [Fact]
    public void EveryEntryRefusesAnEmptyName()
    {
        Assert.Throws<ArgumentException>(() => _registration.Disarm(""));
        Assert.Throws<ArgumentException>(() => _registration.IsArmed(" "));
        Assert.ThrowsAny<ArgumentException>(() => _registration.Read(null!));
        Assert.Throws<ArgumentException>(() => _registration.RemoveResidue("A.exe", ""));
        Assert.Throws<ArgumentNullException>(() => _registration.Arm(null!));
    }
}
