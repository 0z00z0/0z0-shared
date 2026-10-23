using System.Resources;
using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Controls.WinUI;

/// <summary>The controls' own texts, with one host localizer for the whole application.</summary>
public static class ControlTexts
{
    private static readonly ResourceManager Resources = new("ZeroZero.Controls.WinUI.Texts", typeof(ControlTexts).Assembly);

    /// <summary>Asked first for every key; null asks nothing. Set before the first window opens.</summary>
    public static IStringLocalizer? Strings { get; set; }

    internal static IStringLocalizer Current => new LibraryStringLocalizer(Resources, Strings);
}
