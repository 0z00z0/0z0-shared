using System.Reflection;

namespace ZeroZero.Primitives;

/// <summary>The version an assembly reports about itself, read off the loaded assembly rather than
/// compiled in from a constant, and the form an About box shows.</summary>
/// <remarks>A consumer builds against a sibling working tree rather than the revision its pin names,
/// so a value that cannot disagree with the pin answers nothing. The informational version carries
/// the commit as well as the number, and the commit is what identifies a build made between tags,
/// where every revision since the last bump reports the same three digits.</remarks>
public static class AssemblyVersionText
{
    /// <summary>The version <paramref name="assembly"/> carries — <c>0.7.0+1a2b3c4</c> where a commit
    /// was stamped, and <c>0.7.0</c> where none was.</summary>
    /// <remarks>The informational version first, because it is the only one carrying the commit. The
    /// assembly version is the fallback rather than the primary: it is normalised to four parts and
    /// never carries metadata. An assembly with neither reports the empty string rather than a
    /// fabricated number.</remarks>
    public static string Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            is { InformationalVersion: { Length: > 0 } informational })
            return informational;

        return assembly.GetName().Version?.ToString() ?? "";
    }

    /// <summary>The number as it stands, with a commit after the <c>+</c> cut to seven characters.
    /// The SDK's own stamp is the full forty, which no About box has room for. Metadata that is not
    /// a commit, and a version carrying none, pass through untouched.</summary>
    public static string ForDisplay(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        int plus = version.IndexOf('+');
        if (plus < 0) return version;

        ReadOnlySpan<char> revision = version.AsSpan(plus + 1);
        if (revision.Length <= 7 || !IsHex(revision)) return version;

        return string.Concat(version.AsSpan(0, plus + 1), revision[..7]);
    }

    private static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
