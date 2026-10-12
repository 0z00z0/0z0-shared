using System.Globalization;
using System.Resources;
using Jeffijoe.MessageFormat;
using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>The module's own texts, Texts.resx and its translations, behind the host's localizer.</summary>
internal static class MqttTexts
{
    private static readonly ResourceManager Resources = new("ZeroZero.Mqtt.Texts", typeof(MqttTexts).Assembly);

    public static IStringLocalizer With(IStringLocalizer? host) => new LibraryStringLocalizer(Resources, host);

    /// <summary>A counted text: an ICU MessageFormat pattern, its plural chosen by the UI culture.</summary>
    public static string Count(IStringLocalizer texts, string key, IReadOnlyDictionary<string, object?> arguments)
    {
        var culture = CultureInfo.CurrentUICulture;
        var formatter = new MessageFormatter(useCache: false, culture);

        // Under InvariantGlobalization en-GB cannot be created, so its one/other rule stands in.
        if (culture.Name.Length == 0) formatter.CardinalPluralizers![""] = n => n == 1 ? "one" : "other";

        return formatter.FormatMessage(texts[key].Value, arguments);
    }
}
