using System.Reflection;

namespace ZeroZero.Mqtt;

/// <summary>What this build of the module is, read from the assembly that is actually loaded.</summary>
/// <remarks>
/// <para>Consumers take the module as a sibling checkout pinned to a tag, and a local build resolves
/// the working tree rather than the pinned revision — so the pin describes a source tree and not the
/// binary in the process. The whole point of this type is to be able to disagree with the pin, which
/// is why the value is reflected off the loaded assembly rather than compiled in from a constant: a
/// number that cannot disagree answers nothing.</para>
/// <para>The informational version carries the commit as well as the number, because the interesting
/// case is a build made between tags, where every revision since the last bump reports the same
/// three digits.</para>
/// </remarks>
public static class MqttModule
{
    /// <summary>The module's version, as the loaded assembly carries it —
    /// <c>0.5.0+1a2b3c4</c> where a commit was stamped, and <c>0.5.0</c> where none was.</summary>
    public static string Version { get; } = Read(typeof(MqttModule).Assembly);

    /// <summary>The version an arbitrary assembly reports, which is what <see cref="Version"/> asks
    /// of its own. Exposed so a host can report its own build the same way.</summary>
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
}
