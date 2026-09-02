using System.Text.Json;

namespace ZeroZero.Config.Tests;

/// <summary>A throwaway folder per test, and the file under test inside it. xUnit builds one
/// instance per test, so no two tests share a directory.</summary>
public abstract class SettingsFileTestBase : IDisposable
{
    protected const string FileName = "sample.json";

    protected SettingsFileTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "ZeroZero.Config.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The throwaway folder.</summary>
    protected string Root { get; }

    /// <summary>Where the settings file lives.</summary>
    protected string FilePath => Path.Combine(Root, FileName);

    /// <summary>The sibling the atomic save writes first.</summary>
    protected string TempPath => FilePath + ".tmp";

    /// <summary>Holds the settings file open so no other process may touch it, which is what a
    /// locked or read-only file looks like from inside a save — and from inside a load.</summary>
    protected FileStream Seize() => new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    protected SettingsFileOptions Options(
        SettingsFileQuarantine? quarantine = null,
        SynchronizationContext? notificationContext = null) =>
        new(Root, FileName)
        {
            Quarantine = quarantine ?? SettingsFileQuarantine.Default,
            NotificationContext = notificationContext,
        };

    protected SettingsFile<SampleSettings> Create(
        SettingsFileQuarantine? quarantine = null,
        SynchronizationContext? notificationContext = null) =>
        new(Options(quarantine, notificationContext));

    /// <summary>What the file on disk currently deserialises to.</summary>
    protected SampleSettings OnDisk() =>
        JsonSerializer.Deserialize<SampleSettings>(File.ReadAllText(FilePath), SettingsFileOptions.DefaultSerialiser)
        ?? throw new InvalidOperationException("The file on disk deserialised to null.");

    protected string[] QuarantineCopies() =>
        [.. Directory.EnumerateFiles(Root, "sample.*.bad.json").Order(StringComparer.Ordinal)];

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary folder is the operating system's problem, not the test's.
        }

        GC.SuppressFinalize(this);
    }
}
