using System.Reflection;
using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics;

/// <summary>The first line of every run: which build produced the log that follows.</summary>
/// <remarks>The version is the full text the assembly carries, commit included, never the About-box
/// form — a log is read beside a source tree, and the whole revision is what finds the commit.
/// Write it before anything that can throw and before the dump registration, through a sink that
/// writes regardless of level, so the line survives every level setting short of off.</remarks>
public static class StartupVersionLine
{
    /// <summary><c>Name 1.2.3+commit starting</c>; an assembly carrying no version at all gives the
    /// name alone rather than a fabricated number.</summary>
    public static string For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string name = assembly.GetName().Name ?? "";
        string version = AssemblyVersionText.Read(assembly);
        return version.Length == 0 ? $"{name} starting" : $"{name} {version} starting";
    }

    public static void Write(ILogSink sink, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Info(For(assembly));
    }
}
