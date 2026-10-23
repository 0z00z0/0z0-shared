using System.Globalization;
using System.Runtime.CompilerServices;

namespace ZeroZero.Tests;

/// <summary>The cultures a test reads texts in: the UI culture picks the language, the current
/// culture the number and time format. Both are pinned to en-GB for the whole assembly, without the
/// machine's own changes, so English assertions hold on a machine set to any language or format.</summary>
internal static class UiCulture
{
    [ModuleInitializer]
    internal static void PinEnGb()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-GB");
    }

    public static T Run<T>(CultureInfo language, Func<T> read) => Run(language, CultureInfo.CurrentCulture, read);

    public static T Run<T>(CultureInfo language, CultureInfo regionalFormat, Func<T> read)
    {
        var (beforeLanguage, beforeFormat) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (language, regionalFormat);
        try { return read(); }
        finally { (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (beforeLanguage, beforeFormat); }
    }

    /// <summary>en-GB with a comma as the decimal separator, a regional format a person may set.</summary>
    public static CultureInfo EnGbWithDecimalComma()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        return culture;
    }
}
