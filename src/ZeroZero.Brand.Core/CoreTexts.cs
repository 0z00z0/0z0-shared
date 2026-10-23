using System.Resources;
using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Brand.Core;

/// <summary>The console banner's own texts, Texts.resx and its translations, behind the host's localizer.</summary>
internal static class CoreTexts
{
    private static readonly ResourceManager Resources = new("ZeroZero.Brand.Core.Texts", typeof(CoreTexts).Assembly);

    public static IStringLocalizer With(IStringLocalizer? host) => new LibraryStringLocalizer(Resources, host);
}
