using System.Reflection;
using System.Reflection.Emit;

namespace ZeroZero.Tests;

/// <summary>Assemblies built in a test with exactly the version metadata a case needs. Linked into
/// ZeroZero.Diagnostics.Tests as source.</summary>
internal static class TestAssemblies
{
    /// <summary>An assembly carrying the four-part version, and the informational version when one is given.</summary>
    internal static Assembly Built(Version version, string? informational, string? name = null)
    {
        var assemblyName = new AssemblyName(name ?? $"Built.{Guid.NewGuid():N}") { Version = version };
        var builder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        if (informational is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));
        }

        return builder;
    }
}

/// <summary>An assembly with no version of either kind. A dynamic assembly always answers 0.0.0.0, so
/// this case needs a hand-written one.</summary>
internal sealed class UnversionedAssembly : Assembly
{
    public override AssemblyName GetName() => new("Unversioned");

    // Typed Attribute[] at runtime: the framework casts the answer to that before reading it.
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<Attribute>();

    public override object[] GetCustomAttributes(bool inherit) => Array.Empty<Attribute>();

    public override bool IsDefined(Type attributeType, bool inherit) => false;

    public override IList<CustomAttributeData> GetCustomAttributesData() => [];
}
