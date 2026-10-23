using System.Globalization;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>A host text the module's arguments cannot fill gives way to the module's own text, so
/// the panel's lines never throw. Run in en-GB for language and regional format, as a host sets it.</summary>
public class MqttHostTextFormatTests
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(10);

    // The endpoint a connected panel shows: remembered, port 443 over WebSocket, encrypted.
    private static readonly MqttEndpointRequest Request = new("broker.invalid", "user", null, MqttTransportMode.Auto);
    private static readonly MqttEndpointMemory Memory = new("broker.invalid", "user", 443, MqttTransport.WebSocket, Encrypted: true);

    private static string Render(MqttPanelText text, string key) => key switch
    {
        "AgeMinutes" => text.Relative(Now.AddMinutes(-5), Now, ""),
        "AgeHours" => text.Relative(Now.AddHours(-3), Now, ""),
        "AgeDays" => text.Relative(Now.AddDays(-2), Now, ""),
        "SummaryPublish" => text.SummarisePublish(new MqttPublishTally(2, 3)),
        _ => text.SummariseBroker(Request, Memory, MqttConnectionState.Connected, MqttCertificateTrustMode.System).Text,
    };

    private static string InEnGb(Func<string> read) => UiCulture.Run(EnGb, EnGb, read);

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("AgeMinutes", "{0} minutes ago")]
    [InlineData("AgeHours", "{0} hours ago")]
    [InlineData("AgeDays", "{0} days ago")]
    [InlineData("SummaryPublish", "{0} of {1} switched on")]
    [InlineData("SummaryPublish", "{on} of {total")]
    public void AHostPositionalTextForACountedKeyGivesWayToTheModulesOwn(string key, string hostText)
    {
        var host = new MqttPanelText(new FixedLocalizer(new() { [key] = hostText }));

        Assert.Equal(InEnGb(() => Render(MqttPanelText.Default, key)), InEnGb(() => Render(host, key)));
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("{4}")]
    [InlineData("{count, plural, one {# broker} other {# brokers}}")]
    [InlineData("{0")]
    public void AHostTextThePositionalArgumentsCannotFillGivesWayToTheModulesOwn(string hostText)
    {
        var host = new MqttPanelText(new FixedLocalizer(new() { ["SummaryBroker"] = hostText }));

        Assert.Equal(InEnGb(() => Render(MqttPanelText.Default, "SummaryBroker")), InEnGb(() => Render(host, "SummaryBroker")));
    }

    [Fact]
    public void AnUnformattableHostTextIsLoggedOncePerKey()
    {
        var log = new RecordingLogSink();
        var host = new MqttPanelText(new FixedLocalizer(new() { ["SummaryPublish"] = "{0} of {1}" }), log);

        InEnGb(() => Render(host, "SummaryPublish"));
        InEnGb(() => Render(host, "SummaryPublish"));

        Assert.Single(log.Errors);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("en-US")]
    [InlineData("nb-NO")]
    public void TheModulesOwnCountedTextsFormatInEveryCulture(string culture)
    {
        var language = CultureInfo.GetCultureInfo(culture);
        foreach (string key in new[] { "AgeMinutes", "AgeHours", "AgeDays", "SummaryPublish", "SummaryBroker" })
            Assert.DoesNotContain("{", UiCulture.Run(language, language, () => Render(MqttPanelText.Default, key)));
    }
}
