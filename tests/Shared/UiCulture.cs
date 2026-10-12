using System.Globalization;
using System.Runtime.CompilerServices;

namespace ZeroZero.Tests;

/// <summary>The UI culture a test reads texts in, pinned to en-GB for the whole assembly so English
/// assertions hold on a machine set to any language.</summary>
internal static class UiCulture
{
    [ModuleInitializer]
    internal static void PinEnGb() => CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-GB");

    public static T Run<T>(CultureInfo culture, Func<T> read)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try { return read(); }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
