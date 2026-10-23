using System.Resources;
using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Brand.WinUI;

/// <summary>The About surface's own texts, Texts.resx and its translations, behind the host's localizer.</summary>
internal static class BrandTexts
{
    private static readonly ResourceManager Resources = new("ZeroZero.Brand.WinUI.Texts", typeof(BrandTexts).Assembly);

    public static IStringLocalizer With(IStringLocalizer? host) => new LibraryStringLocalizer(Resources, host);
}
