using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ZeroZero.Tests;

/// <summary>The checks every component's resource files are held to: the translator's file must say
/// what each text is, and a translation must carry exactly the placeholders of the neutral text.</summary>
internal static partial class TranslationFiles
{
    public sealed record Entry(string Value, string Comment);

    public static Dictionary<string, Entry> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(
            static d => d.Attribute("name")!.Value,
            static d => new Entry(d.Element("value")?.Value ?? "", d.Element("comment")?.Value ?? ""),
            StringComparer.Ordinal);

    /// <summary>Ordinary placeholders by index and ICU arguments by name. A brace after a plural
    /// selector opens a branch, not an argument, so plural categories may differ between languages.</summary>
    public static SortedSet<string> Placeholders(string text) =>
        new(Argument().Matches(text)
                .Where(m => !(Complex().IsMatch(text) && Selector().IsMatch(text[..m.Index])))
                .Select(static m => m.Groups[1].Value),
            StringComparer.Ordinal);

    public static void AssertSameKeysAndPlaceholders(string neutralPath, string translatedPath)
    {
        var neutral = Read(neutralPath);
        var translated = Read(translatedPath);

        Assert.Equal("", string.Join(", ", neutral.Keys.Except(translated.Keys).Order()));
        Assert.Equal("", string.Join(", ", translated.Keys.Except(neutral.Keys).Order()));

        foreach (var (key, entry) in translated)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{key} is blank in {Path.GetFileName(translatedPath)}.");
            Assert.True(
                Placeholders(neutral[key].Value).SetEquals(Placeholders(entry.Value)),
                $"{key} carries other placeholders in {Path.GetFileName(translatedPath)}: {entry.Value}");
        }
    }

    public static void AssertEveryEntryHasAComment(string path)
    {
        var silent = Read(path).Where(static e => string.IsNullOrWhiteSpace(e.Value.Comment)).Select(static e => e.Key);

        Assert.True(!silent.Any(), $"{Path.GetFileName(path)} has no context note for {string.Join(", ", silent)}.");
    }

    [GeneratedRegex(@"\{\s*([A-Za-z0-9_]+)\s*(?=[,:}])")]
    private static partial Regex Argument();

    [GeneratedRegex(@"(?<![A-Za-z])(?:zero|one|two|few|many|other|=\d+)\s*$")]
    private static partial Regex Selector();

    [GeneratedRegex(@",\s*(?:plural|selectordinal|select)\s*,")]
    private static partial Regex Complex();
}
