using System.Reflection;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>Every <c>Color…</c> constant on <see cref="Brand"/>, read by reflection so a new one reaches every palette test.</summary>
public static class PaletteConstants
{
    public static readonly IReadOnlyDictionary<string, string> All =
        typeof(Brand).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.StartsWith("Color", StringComparison.Ordinal))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

    public static IEnumerable<object[]> Values => All.Values.Select(v => new object[] { v });
}
