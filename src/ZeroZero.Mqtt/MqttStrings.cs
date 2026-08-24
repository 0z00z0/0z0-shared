using System.Globalization;

namespace ZeroZero.Mqtt;

/// <summary>Where a translated string comes from. One member, because that is the whole of what a
/// resource system has to offer the module: a key goes in, either a translation or nothing comes
/// out.</summary>
/// <remarks>Returning null rather than throwing is what keeps the module's own en-GB the floor. A
/// resource map that failed to load, a key a translator has not reached yet and a language with no
/// entry for one string all answer the same way, and all three leave the built-in text standing
/// rather than leaving a control blank.</remarks>
public interface IMqttStringSource
{
    string? Find(string key);
}

/// <summary>Every user-facing string the module owns, keyed, with its en-GB text built in. A host
/// localises by supplying an <see cref="IMqttStringSource"/>; anything the source does not answer
/// falls back to the text here.</summary>
/// <remarks>
/// <para>The keys are flat identifiers on purpose. A <c>.resw</c> name treats the segment after a
/// dot as a property of a named element, so <c>Status.Age</c> would be read as the <c>Age</c>
/// property of an element called <c>Status</c> rather than as one string.</para>
/// <para>Every entry with a placeholder is a format string rather than a sentence assembled from
/// parts, so a translator can reorder it. Nothing here is concatenated at a call site.</para>
/// </remarks>
public sealed class MqttStrings
{
    /// <summary>The module's own en-GB, with no source behind it.</summary>
    public static MqttStrings Default { get; } = new(source: null);

    private readonly IMqttStringSource? _source;

    public MqttStrings(IMqttStringSource? source) => _source = source;

    /// <summary>The text for a key: the source's, the built-in en-GB, or the key itself when a
    /// caller asks for something that does not exist — visible in a screenshot rather than
    /// silent.</summary>
    public string Get(string key) =>
        _source?.Find(key) is { Length: > 0 } translated ? translated
        : Builtin.TryGetValue(key, out string? text) ? text
        : key;

    /// <summary>A format string filled for the current culture, which is what a displayed value is
    /// always formatted for.</summary>
    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The en-GB text behind every key. Public so a consumer generating a translation
    /// template has the full set without reading the source.</summary>
    public static IReadOnlyDictionary<string, string> Builtin { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Relative ages. Singular and plural are separate keys chosen in code: .NET carries no
            // plural rules, and a language needing more than two forms is where a library would
            // start earning its place.
            ["AgeJustNow"]  = "just now",
            // "min" is an abbreviation, so it does not pluralise in English; a translator whose
            // language disagrees has its own key to change.
            ["AgeMinutes"]  = "{0} min ago",
            ["AgeHour"]     = "{0} hour ago",
            ["AgeHours"]    = "{0} hours ago",
            ["AgeDay"]      = "{0} day ago",
            ["AgeDays"]     = "{0} days ago",

            // Status values.
            ["StatusNoHost"]           = "No broker host set",
            ["StatusProbing"]          = "Looking for the broker",
            ["StatusBrokerNotSet"]     = "Not set",
            ["StatusBrokerUnsettled"]  = "{0} — not connected yet",
            ["StatusBrokerInUse"]      = "{0}:{1} over {2}",
            ["StatusNothingPublished"] = "Nothing published yet",
            ["StatusNothingReceived"]  = "Nothing received yet",
            ["StatusLastCommand"]      = "{0} — {1}",

            // How a transport and a connection state are named.
            ["TransportTcp"]       = "TCP",
            ["TransportWebSocket"] = "WebSocket",
            ["StateDisabled"]      = "Not publishing",
            ["StateSearching"]     = "Looking for the broker",
            ["StateConnecting"]    = "Connecting",
            ["StateConnected"]     = "Connected",
            ["StateRetrying"]      = "Reconnecting",
            ["StateNotConnected"]  = "Not connected",

            // Where the endpoint in force came from, and whether the link is in clear text.
            ["ProvenanceAutomatic"]    = "Automatically detected",
            ["ProvenanceManual"]       = "Set manually",
            ["ProvenanceEncrypted"]    = "{0} — encrypted",
            ["ProvenanceNotEncrypted"] = "{0} — not encrypted",

            // One candidate's verdict, named for the transport it was reached over.
            ["ProbeSuccess"]        = "Connected over {0}. The broker accepted these settings.",
            ["ProbeUnreachable"]    = "Could not reach the broker over {0} — {1}.",
            ["ProbeTimedOut"]       = "The broker did not answer over {0} within {1} seconds.",
            ["ProbeAuthRejected"]   = "The broker answered over {0} but rejected these credentials ({1}).",
            ["ProbeRejected"]       = "The broker refused the connection over {0} ({1}).",
            ["ProbeTlsUntrusted"]   = "The encrypted connection over {0} was not established — {1}. Check the certificate trust setting.",
            ["ProbeTlsUnsupported"] = "The broker does not accept encrypted connections over {0} on that port — {1}.",
            ["ProbeFailed"]         = "The connection over {0} failed — {1}.",

            // The same verdicts with nothing naming the endpoint, for the sentences that carry more
            // than one.
            ["ClauseSuccess"]        = "connected",
            ["ClauseUnreachable"]    = "could not be reached ({0})",
            ["ClauseTimedOut"]       = "did not answer",
            ["ClauseAuthRejected"]   = "rejected these credentials",
            ["ClauseRejected"]       = "was refused ({0})",
            ["ClauseTlsUntrusted"]   = "refused the encrypted connection ({0})",
            ["ClauseTlsUnsupported"] = "does not accept encrypted connections there",
            ["ClauseFailed"]         = "failed ({0})",

            // The sweep, as it happens.
            ["ProgressEndpoint"]  = "{0} on port {1}",
            ["ProgressPort"]      = "Trying {0}…",
            ["ProgressTransport"] = "Trying {0} — asking the broker…",
            ["ProgressFinished"]  = "{0} {1}.",
            ["ProgressNoAnswer"]  = "{0} — no answer recorded.",

            // The whole run.
            ["ReportNoHost"]         = "No broker host set.",
            ["ReportWithContext"]    = "{0} {1}.",
            ["ReportNothingReached"] = "Neither transport reached the broker. {0}.",
            ["ReportFragment"]       = "{0} {1}",
            ["ReportFragmentJoin"]   = "; ",

            // Panel state that is neither a status value nor a probe sentence.
            ["TestRunning"]      = "Testing…",
            ["Applied"]          = "Applied.",
            ["Saved"]            = "Saved.",
            ["NotApplied"]       = "Not applied",
            ["PublishFailed"]    = "Nothing reached the broker",
            ["PortOutOfRange"]   = "Port must be between {0} and {1}.",
            ["PortNotANumber"]   = "Port must be a whole number between {0} and {1}.",
            ["OptionAutomatic"]  = "Automatic",
            ["PortCustom"]       = "Other…",
            ["ToggleOn"]         = "On",
            ["ToggleOff"]        = "Off",

            // The device-id dialogue. The acknowledgement names the mechanism rather than one
            // application's consequence of it, so it stays true for a consumer with no automations;
            // a host with a sharper consequence supplies its own line beside it.
            ["DeviceIdTitle"]     = "Change device ID",
            ["DeviceIdConfirm"]   = "Change ID",
            ["DeviceIdCancel"]    = "Cancel",
            ["DeviceIdCurrent"]   = "Current ID: {0}",
            ["DeviceIdNew"]       = "New ID",
            ["DeviceIdPreview"]   = "Publishes as: {0}",
            ["DeviceIdWarning"]   = "Changing the ID renames every entity this application publishes. "
                                  + "Anything pointing at the old entities stops resolving — no error is "
                                  + "reported, the entities are simply no longer there.\n\n"
                                  + "The old entities are removed from the broker on confirmation. Their "
                                  + "recorded history is not carried over to the new ones.\n\n"
                                  + "An empty box restores the name derived from this machine.",
            ["DeviceIdAcknowledge"] = "I understand everything referring to the old ID stops working",

            // Section headings and row labels. Held here rather than only in markup so the composed
            // text and the static text localise through one mechanism.
            ["HeadingStatus"]        = "Status",
            ["HeadingDevice"]        = "Device",
            ["HeadingBroker"]        = "Broker",
            ["HeadingPublish"]       = "What to publish",
            ["RowConnection"]        = "Connection",
            ["RowBrokerInUse"]       = "Broker in use",
            ["RowLastPublish"]       = "Last publish",
            ["RowLastCommand"]       = "Last command received",
            ["RowDeviceName"]        = "Device name",
            ["RowDeviceId"]          = "Device ID",
            ["RowHost"]              = "Host",
            ["RowPort"]              = "Port",
            ["RowTransport"]         = "Transport",
            ["RowEncryption"]        = "Encrypted connection",
            ["RowUsername"]          = "Username",
            ["RowPassword"]          = "Password",
            ["RowDiscoveryPrefix"]   = "Discovery prefix",
            ["RowApply"]             = "Apply these settings",
            ["ButtonApply"]          = "Apply",
            ["ButtonTest"]           = "Test connection",
            ["ButtonPublishNow"]     = "Publish now",
            ["ButtonChangeDeviceId"] = "Change ID…",

            // Placeholders. A host name has to be an example rather than an address.
            ["PlaceholderHost"] = "e.g. mqtt.example.com",
            ["PlaceholderPort"] = "1-65535",

            // Row descriptions: the one-line consequence, never a restatement of the label. A row
            // whose description could only repeat its own header — Host — has none, because it reads
            // more clearly without one. A second sentence appears only where it carries a
            // consequence that is not obvious and is destructive.
            ["DescPort"]            = "Automatic tries the standard ports.",
            ["DescTransport"]       = "TCP direct, or WebSocket through a proxy.",
            ["DescEncryption"]      = "Encrypts traffic to the broker.",
            ["DescUsername"]        = "Leave blank for anonymous access.",
            ["DescPassword"]        = "Leave blank for anonymous access.",
            ["DescDiscoveryPrefix"] = "Prefix for discovery topics. Change only to match the broker.",
            ["DescDeviceName"]      = "Shown wherever this device appears.",
            ["DescDeviceId"]        = "Used in every topic and entity id. Changing it moves them all.",
            ["DescBroker"]          = "How the broker is reached. Changes take effect on Apply.",
            ["DescApply"]           = "Reconnects once.",
            ["DescPublish"]         = "Which entities are announced.",
            ["DescPublishSwitch"]   = "Publishes this application's state to an MQTT broker.",
            ["TitlePublishSwitch"]  = "Publish to MQTT",

            // Info-icon text: the explanation the row is clearer without. Protocol vocabulary is
            // identical for every consumer, so a host never writes what a transport is; what an
            // application publishes is the opposite, and none of it is here.
            ["InfoPublishSwitch"] = "The master switch for the whole feature. Nothing on the network is "
                                  + "touched while it is off, and the rest of this panel is hidden because "
                                  + "none of it applies. This switch takes effect immediately; the broker "
                                  + "fields take effect on Apply.",
            ["InfoStatus"]        = "Read-only, and about the live connection rather than the fields below. "
                                  + "The two ages re-read themselves while this page is open.",
            ["InfoConnection"]    = "Where the broker settings in force came from, and whether the live link "
                                  + "is encrypted. A port, transport or encryption left on Automatic is found "
                                  + "by probing the host, and the answer is remembered against that host and "
                                  + "user name. Setting one by hand pins it, and nothing is probed around it. "
                                  + "Automatic can settle on clear text on its own, which is why this row "
                                  + "says which it is.",
            ["InfoBrokerInUse"]   = "The address the live connection is using. The saved values, not the ones "
                                  + "staged in the fields below; the two differ until Apply.",
            ["InfoLastPublish"]   = "When a message last reached the broker. Unchanged values are not re-sent, "
                                  + "so this stands still while nothing changes. Publish now sends the current "
                                  + "state for the groups switched on below; it announces nothing and "
                                  + "re-declares no entity.",
            ["InfoLastCommand"]   = "The most recent command the broker sent. Commands arrive on the topics "
                                  + "the announced entities subscribe to. Nothing is shown until one is "
                                  + "acted on.",
            ["InfoDevice"]        = "How this machine appears to whatever consumes the entities: one display "
                                  + "name, and one identifier every topic is built from.",
            ["InfoDeviceName"]    = "Cosmetic — it renames nothing else. An empty box falls back to the name "
                                  + "derived from this machine, shown as the placeholder. Saved as it is "
                                  + "typed, not behind Apply.",
            ["InfoDeviceId"]      = "Changing it renames every published entity, so it has a confirmation of "
                                  + "its own rather than riding the Apply batch.",
            ["InfoBroker"]        = "These fields are staged locally and take effect on Apply, so the "
                                  + "connection is remade once per edit session rather than once per "
                                  + "keystroke. Port, transport and encryption left on Automatic are found by "
                                  + "probing, which happens when one of these fields settles and when Test "
                                  + "connection or Apply is pressed — never merely on opening the page.",
            ["InfoHost"]          = "A name or an address. A host written as a ws:// or wss:// address is used "
                                  + "exactly as typed, which covers a broker served under a path the port "
                                  + "alone cannot express.",
            ["InfoPort"]          = "Automatic tries the ports brokers are commonly served on, in turn, and "
                                  + "remembers the one that answered. The list covers plain MQTT, MQTT over "
                                  + "encryption, and the front-door ports a broker published over WebSocket "
                                  + "answers on. The last entry takes a port typed by hand.",
            ["InfoTransport"]     = "Automatic tries a plain socket first and falls back to WebSocket, then "
                                  + "remembers whichever answered and starts there next time — the usual case "
                                  + "for a machine that is on the internal network sometimes and outside it "
                                  + "at others. An explicit choice is never reached around.",
            ["InfoEncryption"]    = "Automatic tries each endpoint encrypted first and only then in clear "
                                  + "text. A broker that answers and refuses the credentials ends the search, "
                                  + "so a wrong password never causes a retry in clear text; nor does a "
                                  + "broker that offered a certificate this machine does not trust. Only an "
                                  + "endpoint with no encryption on offer at all is retried plain. An "
                                  + "explicit choice is never reached around, and a broker reached over "
                                  + "WebSocket through a public front door is encrypted by its address rather "
                                  + "than by this setting.",
            ["InfoUsername"]      = "Part of what a found endpoint is remembered against: a broker commonly "
                                  + "fronts a separate listener per account, so changing the user name starts "
                                  + "the search again.",
            ["InfoPassword"]      = "Never written to the log, never published, and never part of what a "
                                  + "found endpoint is remembered against.",
            ["InfoDiscoveryPrefix"] = "Entities are announced using the Home Assistant MQTT Discovery "
                                  + "convention, an openly published specification that some MQTT consumers "
                                  + "follow and others ignore. The prefix only needs changing for a consumer "
                                  + "that listens elsewhere.",
            ["InfoApply"]         = "Commits every field above as one change and remakes the connection once. "
                                  + "Nothing above is live until it is pressed.",
            ["InfoTest"]          = "Tries the values in the fields above, including ones not yet applied, "
                                  + "over a throwaway connection of its own. It commits nothing at all: not "
                                  + "the fields, and not where the broker answered.",
        };
}
