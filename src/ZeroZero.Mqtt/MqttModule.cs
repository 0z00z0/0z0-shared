using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>What this build of the module is, read from the assembly that is actually loaded.</summary>
/// <remarks>
/// <para>Consumers take the module as a sibling checkout pinned to a tag, and a local build resolves
/// the working tree rather than the pinned revision — so the pin describes a source tree and not the
/// binary in the process. The whole point of this type is to be able to disagree with the pin, which
/// is why the value is reflected off the loaded assembly rather than compiled in from a constant: a
/// number that cannot disagree answers nothing.</para>
/// <para>A host reports its own build the same way through
/// <see cref="AssemblyVersionText.Read(System.Reflection.Assembly)"/>.</para>
/// </remarks>
public static class MqttModule
{
    /// <summary>The module's version, as the loaded assembly carries it —
    /// <c>0.5.0+1a2b3c4</c> where a commit was stamped, and <c>0.5.0</c> where none was.</summary>
    public static string Version { get; } = AssemblyVersionText.Read(typeof(MqttModule).Assembly);
}
