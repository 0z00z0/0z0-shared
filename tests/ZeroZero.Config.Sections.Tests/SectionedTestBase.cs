using System.Text;
using System.Text.Json;

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

    /// <summary>The reader a consuming application uses: a comment fails the whole file.</summary>
    protected static readonly JsonReaderOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow };

    /// <summary>Every comment in the bytes, in file order. Only a comment-allowing reader can tell one
    /// from the same characters inside a string.</summary>
    protected static List<string> CommentsIn(byte[] content)
    {
        var reader = new Utf8JsonReader(content, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });
        var found = new List<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment) found.Add(reader.GetComment());
        }

        return found;
    }

    protected static bool Parses(byte[] content, JsonReaderOptions options)
    {
        try
        {
            var reader = new Utf8JsonReader(content, options);
            while (reader.Read())
            {
                // Reading to the end is the check: the reader throws on what it will not accept.
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A value with the whitespace between its tokens taken out, the only thing a carry
    /// across is allowed to change.</summary>
    protected static string Compact(byte[] content, int start, int end)
    {
        var text = new StringBuilder();
        var reader = new Utf8JsonReader(content.AsSpan(start..end), JsonObjectSpans.ReaderOptions);
        while (reader.Read()) text.Append(reader.TokenType).Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append('|');
        return text.ToString();
    }

    /// <summary>The reader shape the consuming application uses: the declared spelling and nothing else.</summary>
    protected static JsonSerializerOptions CaseSensitive()
    {
        var options = SettingsFileOptions.CreateSerialiser();
        options.PropertyNameCaseInsensitive = false;
        return options;
    }

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
