using System.Text;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>A throwaway folder per test, and the document under test inside it. xUnit builds one
/// instance per test, so no two tests share a directory.</summary>
public abstract class SectionedTestBase : IDisposable
{
    protected const string FileName = "settings.json";

    protected SectionedTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "ZeroZero.Config.Sections.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The throwaway folder.</summary>
    protected string Root { get; }

    /// <summary>Where the settings document lives.</summary>
    protected string FilePath => Path.Combine(Root, FileName);

    /// <summary>The order a section takes when the document does not carry it yet.</summary>
    protected static string[] Order => ["general", "graph", "window"];

    /// <summary>Holds the document open so no other process may touch it, which is what a locked file
    /// looks like from inside a read and from inside a write.</summary>
    protected FileStream Seize() => new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    protected SectionedSettingsOptions Options(
        SettingsFileQuarantine? quarantine = null,
        SynchronizationContext? notificationContext = null,
        int version = 1) =>
        new(Root, FileName)
        {
            Quarantine = quarantine ?? SettingsFileQuarantine.Default,
            NotificationContext = notificationContext,
            Version = version,
            SectionOrder = Order,
        };

    protected SectionedSettingsFile Create(
        SettingsFileQuarantine? quarantine = null,
        SynchronizationContext? notificationContext = null,
        int version = 1) =>
        new(Options(quarantine, notificationContext, version));

    /// <summary>Writes the document exactly as given, byte for byte.</summary>
    protected void Given(string content) => File.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes(content));

    protected void GivenBytes(byte[] content) => File.WriteAllBytes(FilePath, content);

    /// <summary>The document as it now stands on disk, byte for byte.</summary>
    protected string OnDisk() => Encoding.UTF8.GetString(File.ReadAllBytes(FilePath));

    protected byte[] OnDiskBytes() => File.ReadAllBytes(FilePath);

    protected string[] QuarantineCopies() =>
        [.. Directory.EnumerateFiles(Root, "settings.*.bad.json").Order(StringComparer.Ordinal)];

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
