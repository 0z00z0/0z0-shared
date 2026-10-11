using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// Copies of a real packed artefact with one thing changed. Each copy stays a valid package with a
/// valid nuspec, which is the point: the verifier must refuse it on truth, not on shape.
/// </summary>
internal static class Artefacts
{
    public static string CopyWithEntryText(string package, string target, string entryName, Func<string, string> rewrite) =>
        CopyWithEntryBytes(package, target, entryName, bytes =>
        {
            var text = new UTF8Encoding(false).GetString(StripBom(bytes));
            var replaced = rewrite(text);
            if (replaced == text) throw new InvalidOperationException($"The rewrite changed nothing in {entryName}.");
            return new UTF8Encoding(false).GetBytes(replaced);
        });

    public static string CopyWithEntryBytes(string package, string target, string entryName, Func<byte[], byte[]> rewrite)
    {
        File.Copy(package, target, overwrite: true);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} is not in {package}.");
        byte[] bytes;
        using (var stream = entry.Open())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        var replaced = rewrite(bytes);
        entry.Delete();
        using var fresh = zip.CreateEntry(entryName).Open();
        fresh.Write(replaced);
        return target;
    }

    /// <summary>A copy of the package with one entry removed.</summary>
    public static string CopyWithoutEntry(string package, string target, string entryName)
    {
        File.Copy(package, target, overwrite: true);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"{entryName} is not in {package}.");
        entry.Delete();
        return target;
    }

    /// <summary>Every occurrence of one byte sequence replaced by another of the same length.</summary>
    public static byte[] ReplaceAll(byte[] bytes, byte[] find, byte[] replacement, out int occurrences)
    {
        if (find.Length != replacement.Length) throw new ArgumentException("Same length only, so the file's layout is untouched.");
        var copy = (byte[])bytes.Clone();
        occurrences = 0;
        var span = copy.AsSpan();
        var start = 0;
        while (true)
        {
            var index = span[start..].IndexOf(find);
            if (index < 0) break;
            replacement.CopyTo(span[(start + index)..]);
            occurrences++;
            start += index + find.Length;
        }
        return copy;
    }

    /// <summary>
    /// A copy of the record. With a package given, the artefact's hash is recomputed from it, so
    /// the bytes assertion passes and whatever else the copy says is what gets refused.
    /// </summary>
    public static string Record(string recordPath, string target, string? hashOfPackage = null, Action<JsonObject>? edit = null)
    {
        var record = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject()
            ?? throw new InvalidOperationException($"{recordPath} is not a JSON object.");
        if (hashOfPackage is not null)
        {
            record["artefacts"]![0]!["sha256"] = PackedRelease.Sha256(hashOfPackage);
        }
        edit?.Invoke(record);
        File.WriteAllText(target, record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return target;
    }

    /// <summary>One file published to a folder feed under <paramref name="root"/>, with a record that
    /// lists it at its real hash.</summary>
    public static (string Feed, string Record) Publish(string root, string file, string name, string tag, string commit)
    {
        var feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        File.Copy(file, Path.Combine(feed, name));
        var record = Path.Combine(root, "release-artefacts.json");
        File.WriteAllText(record, JsonSerializer.Serialize(new
        {
            tag,
            version = "1.0.0",
            commit,
            artefacts = new[] { new { name, sha256 = PackedRelease.Sha256(file) } },
        }));
        return (feed, record);
    }

    /// <summary>The subject of the certificate that signed <paramref name="file"/>.</summary>
    public static string SignerSubjectOf(string file)
    {
        var result = Scripts.Command($"(Get-AuthenticodeSignature -LiteralPath {Scripts.Quote(file)}).SignerCertificate.Subject");
        Assert.True(result.Passed, result.ToString());
        return result.Output.Trim();
    }

    public static string RecordedHash(string recordPath) =>
        JsonNode.Parse(File.ReadAllText(recordPath))!["artefacts"]![0]!["sha256"]!.GetValue<string>();

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;
}
