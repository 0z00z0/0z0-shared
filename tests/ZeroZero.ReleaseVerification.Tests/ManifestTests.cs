using Xunit;
using System.Text;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The rewriter pair in manifest.ps1: a key matches exactly one line, at any indentation, as a
/// list item, never in a comment; the value is written literally and the file's endings are kept.
/// </summary>
public sealed class ManifestTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "zz-manifest-" + Guid.NewGuid().ToString("n"));

    private const string Template =
        "PackageIdentifier: ZeroZero.Primitives\n" +
        "PackageVersion: {Version}\n" +
        "Installers:\n" +
        "  - Architecture: x64\n" +
        "    InstallerType: nullsoft\n" +
        "    InstallerUrl: {InstallerUrl}\n" +
        "    InstallerSha256: {InstallerSha256}\n" +
        "# InstallerUrl: a comment that must not count\n" +
        "ManifestType: installer\n";

    public ManifestTests() => Directory.CreateDirectory(root);

    private string Write(string text, string name = "manifest.yaml", bool bom = false)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        return path;
    }

    private static ScriptResult Get(string path, string key) =>
        Scripts.Manifest($"Get-ManifestValue -Path {Scripts.Quote(path)} -Key {Scripts.Quote(key)}");

    private static ScriptResult Set(string path, string key, string value) =>
        Scripts.Manifest($"Set-ManifestValue -Path {Scripts.Quote(path)} -Key {Scripts.Quote(key)} -Value {Scripts.Quote(value)}");

    [Fact]
    public void Reads_a_key_under_a_list_item_at_depth()
    {
        // The measured cause of frozen manifests: a pattern anchored at column zero that never
        // matched the indented key. The reader finds it where it sits.
        var path = Write(Template);

        var result = Get(path, "InstallerUrl");

        Assert.True(result.Passed, result.ToString());
        Assert.Equal("{InstallerUrl}", result.Output.Trim());
    }

    [Fact]
    public void Rewrites_that_line_and_nothing_else()
    {
        var path = Write(Template);

        var result = Set(path, "InstallerUrl", "https://example.invalid/d/x.exe?v=1");

        Assert.True(result.Passed, result.ToString());
        Assert.Equal(Template.Replace("{InstallerUrl}", "https://example.invalid/d/x.exe?v=1"), File.ReadAllText(path));
    }

    [Fact]
    public void Writes_regex_characters_literally()
    {
        var path = Write(Template);
        const string value = "a$1\\b(c).*[d]{e}";

        var result = Set(path, "InstallerSha256", value);

        Assert.True(result.Passed, result.ToString());
        Assert.Equal(value, Get(path, "InstallerSha256").Output.Trim());
    }

    [Fact]
    public void Refuses_a_key_that_matches_no_line()
    {
        // A rewrite that matches nothing must not report success and leave the placeholder in place.
        var path = Write(Template);

        var result = Set(path, "InstallerURL", "https://example.invalid/x.exe");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("'InstallerURL' matches no line", result.Output);
        Assert.Equal(Template, File.ReadAllText(path));
    }

    [Fact]
    public void Refuses_a_key_that_matches_two_lines()
    {
        var path = Write(Template + "  - Architecture: arm64\n    InstallerUrl: {InstallerUrlArm64}\n");

        var result = Get(path, "InstallerUrl");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("'InstallerUrl' matches 2 lines", result.Output);
    }

    [Fact]
    public void Does_not_match_a_key_that_is_a_prefix_of_another()
    {
        var path = Write("InstallerUrlSuffix: x\nInstallerUrl2: y\n");

        var result = Get(path, "InstallerUrl");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("matches no line", result.Output);
    }

    [Fact]
    public void Does_not_match_a_commented_line()
    {
        var path = Write("# InstallerUrl: only in a comment\nManifestType: installer\n");

        var result = Get(path, "InstallerUrl");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("matches no line", result.Output);
    }

    [Fact]
    public void Keeps_crlf_line_endings()
    {
        var path = Write(Template.Replace("\n", "\r\n"));

        var result = Set(path, "PackageVersion", "1.2.3");

        Assert.True(result.Passed, result.ToString());
        var text = File.ReadAllText(path);
        Assert.DoesNotMatch("(?<!\r)\n", text);
        Assert.EndsWith("\r\n", text);
        Assert.Contains("PackageVersion: 1.2.3\r\n", text);
    }

    [Fact]
    public void Keeps_lf_line_endings()
    {
        var path = Write(Template);

        var result = Set(path, "PackageVersion", "1.2.3");

        Assert.True(result.Passed, result.ToString());
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void Refuses_to_rewrite_a_file_with_mixed_line_endings()
    {
        var mixed = Template.Replace("Installers:\n", "Installers:\r\n");
        var path = Write(mixed);

        var result = Set(path, "PackageVersion", "1.2.3");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("mixes CRLF and LF", result.Output);
        Assert.Equal(mixed, File.ReadAllText(path));
    }

    [Fact]
    public void Keeps_the_byte_order_mark()
    {
        var path = Write(Template, bom: true);

        var result = Set(path, "PackageVersion", "1.2.3");

        Assert.True(result.Passed, result.ToString());
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("1.2.3", Get(path, "PackageVersion").Output.Trim());
    }

    [Fact]
    public void Adds_the_space_a_bare_key_lacks()
    {
        var path = Write("PackageVersion:\nManifestType: installer\n");

        var result = Set(path, "PackageVersion", "1.2.3");

        Assert.True(result.Passed, result.ToString());
        Assert.Equal("PackageVersion: 1.2.3\nManifestType: installer\n", File.ReadAllText(path));
    }

    [Fact]
    public void Refuses_a_value_with_a_line_break()
    {
        var path = Write(Template);

        var result = Set(path, "PackageVersion", "1.2.3\nInstallerUrl: injected");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("line break", result.Output);
        Assert.Equal(Template, File.ReadAllText(path));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }
}
