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
            ["ProbeSuccess"]        = "Connected over {0}.",
            ["ProbeUnreachable"]    = "Could not reach the broker over {0} — {1}.",
            ["ProbeTimedOut"]       = "The broker did not answer over {0} within {1} seconds.",
            ["ProbeAuthRejected"]   = "The broker rejected these credentials over {0} ({1}).",
            ["ProbeRejected"]       = "The broker refused the connection over {0} ({1}).",
            ["ProbeTlsUntrusted"]   = "The encrypted connection over {0} failed — {1}.",
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
            ["ProgressNoAnswer"]  = "{0} — no answer.",

            // The whole run.
            ["ReportNoHost"]         = "No broker host set.",
            ["ReportConnected"]      = "Connected over {0} on port {1}.",
            ["ReportNothingReached"] = "The broker was not reached. {0}.",
            ["ReportFragment"]       = "{0} {1}",
            ["ReportFragmentJoin"]   = "; ",

            // Panel state that is neither a status value nor a probe sentence.
            ["TestRunning"]      = "Testing…",
            ["Applied"]          = "Applied",
            ["Saved"]            = "Saved",
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
                                  + "Anything pointing at the old names stops working, with no error "
                                  + "reported anywhere.\n\n"
                                  + "The old entities are removed from the broker, and their recorded "
                                  + "history does not carry over to the new ones.\n\n"
                                  + "Leaving the box empty falls back to the name derived from this "
                                  + "machine.",
            ["DeviceIdAcknowledge"] = "I understand that anything using the old ID stops working, and its "
                                  + "recorded history is not carried over",

            // Section headings and row labels. Held here rather than only in markup so the composed
            // text and the static text localise through one mechanism.
            ["HeadingStatus"]        = "Status",
            ["HeadingDevice"]        = "Device",
            ["HeadingBroker"]        = "Broker",
            ["HeadingPublish"]       = "What to publish",
            ["RowConnection"]        = "Connection",
            ["RowBrokerInUse"]       = "Broker in use",
            ["RowLastPublish"]       = "Last publish",
            ["RowLastCommand"]       = "Last command accepted",
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
            ["DescDiscoveryPrefix"] = "Prefix for discovery topics. Change only for a consumer that listens elsewhere.",
            ["DescDeviceName"]      = "Shown wherever this device appears.",
            ["DescDeviceId"]        = "Used in every topic and entity id. Changing it moves them all.",
            ["DescApply"]           = "Saves every broker field above and reconnects once. Nothing above is live until then.",
            ["DescPublishSwitch"]   = "Publishes this application's state to an MQTT broker.",
            ["TitlePublishSwitch"]  = "Publish to MQTT",

            // Collapsed-section summaries: what is configured, so a section can be read without
            // being opened. The Broker line carries the instruction, never the outcome — a value
            // left on Automatic is shown marked, so it cannot be read as one that was chosen, and a
            // field with nothing behind it yet stands as the bare instruction rather than an empty
            // bracket.
            ["SummaryBrokerNotSet"]    = "No broker set",
            ["SummaryBroker"]          = "{0} · {1} · {2} · {3}",
            ["SummaryDetected"]        = "{0} (detected)",
            ["SummaryEncrypted"]       = "encrypted",
            ["SummaryNotEncrypted"]    = "not encrypted",
            ["SummaryPublish"]         = "{0} of {1} switched on",
            ["SummaryPublishNoGroups"] = "Nothing to switch on or off",

            // Info-icon text: the explanation the row is clearer without. Protocol vocabulary is
            // identical for every consumer, so a host never writes what a transport is; what an
            // application publishes is the opposite, and none of it is here. Each icon says the one
            // thing its row does not, once — a fact repeated across icons is a fact neither copy is
            // trusted on — and none of them explains why the panel is built the way it is.
            ["InfoPublishSwitch"] = "The master switch for the whole feature. Nothing on the network is "
                                  + "touched while it is off, and it takes effect immediately.",
            ["InfoStatus"]        = "Read-only, and about the live connection rather than the fields below.",
            ["InfoConnection"]    = "Where the broker settings in force came from, and whether the live "
                                  + "link is encrypted — a setting left on Automatic can land on either.",
            ["InfoBrokerInUse"]   = "The address the live connection is using — the saved settings, not "
                                  + "any unapplied edit in the fields below.",
            ["InfoLastPublish"]   = "When a message last reached the broker. Unchanged values are not "
                                  + "re-sent, so this stands still while nothing changes. Publish now "
                                  + "sends the current state for the groups switched on below.",
            ["InfoLastCommand"]   = "The most recent command the broker sent that was acted on. One that "
                                  + "was refused never appears here.",
            ["InfoDevice"]        = "How this machine appears to anything reading its entities: a display "
                                  + "name, and the identifier every topic is built from.",
            ["InfoDeviceName"]    = "Cosmetic — it renames nothing else. An empty box falls back to the "
                                  + "name derived from this machine, shown as the placeholder. Saved when "
                                  + "the box is left, not on Apply.",
            ["InfoDeviceId"]      = "Lower-case letters, digits and underscores only, up to {0} characters. "
                                  + "An empty value falls back to the name derived from this machine.",
            ["InfoBroker"]        = "Changes here take effect on Apply, which remakes the connection once. "
                                  + "Anything left on Automatic is found by trying the broker when Test "
                                  + "connection or Apply is pressed, and at no other time.",
            ["InfoHost"]          = "A name or an address. A full ws:// or wss:// address is used exactly "
                                  + "as typed, including any path.",
            ["InfoPort"]          = "Automatic tries the ports brokers commonly use, encrypted and plain, "
                                  + "and remembers the one that answered. Choose Other… to type a port.",
            ["InfoTransport"]     = "Automatic tries TCP first, then WebSocket, and remembers whichever "
                                  + "answered — useful for a machine that is on the internal network "
                                  + "sometimes and outside it at others. A transport chosen by hand is "
                                  + "always used.",
            ["InfoEncryption"]    = "Automatic tries each endpoint encrypted first, and falls back to clear "
                                  + "text only where the broker offers no encryption at all — a rejected "
                                  + "password or an untrusted certificate ends the search instead. A choice "
                                  + "made by hand is always used, and a host typed as a wss:// address is "
                                  + "encrypted whatever this is set to.",
            ["InfoUsername"]      = "Changing the username starts the search for the broker endpoint "
                                  + "again: a broker often serves a separate listener per account.",
            ["InfoPassword"]      = "Never written to the log, and never published.",
            ["InfoDiscoveryPrefix"] = "Entities are announced using the Home Assistant MQTT Discovery "
                                  + "convention, an openly published specification that some MQTT consumers "
                                  + "follow and others ignore. Change the prefix only for a consumer that "
                                  + "listens elsewhere.",
            ["InfoTest"]          = "Tries the values in the fields above, including ones not yet applied, "
                                  + "over a throwaway connection of its own. It saves nothing at all: not "
                                  + "the fields, and not where the broker answered. Anything left on "
                                  + "Automatic may take several addresses to find; the result names the one "
                                  + "that answered, and lists what was tried only when none of them did.",

            // The module's own version, read from the loaded assembly and shown behind the publish
            // switch's icon. A build made between tags carries the same number as the tag before it,
            // so the commit is the half that identifies a binary.
            ["ModuleVersion"]     = "MQTT module {0}.",

            // The screen-reader name of an info icon: what the icon is about, spoken before its text.
            // Held here because a panel translated everywhere except the naming of its icons is a
            // panel translated for everyone except the people who most depend on that naming.
            ["SubjectPublishSwitch"]   = "the publish switch",
            ["SubjectPublishGroups"]   = "the publishing groups",
            ["SubjectStatus"]          = "the status block",
            ["SubjectConnection"]      = "how the connection was found",
            ["SubjectBrokerInUse"]     = "the broker in use",
            ["SubjectLastPublish"]     = "the last publish time",
            ["SubjectLastCommand"]     = "the last command accepted",
            ["SubjectDevice"]          = "the device section",
            ["SubjectDeviceName"]      = "the device name",
            ["SubjectDeviceId"]        = "the device ID",
            ["SubjectBroker"]          = "the broker section",
            ["SubjectHost"]            = "the broker host",
            ["SubjectPort"]            = "the broker port",
            ["SubjectTransport"]       = "the transport",
            ["SubjectEncryption"]      = "the encrypted connection",
            ["SubjectUsername"]        = "the broker username",
            ["SubjectPassword"]        = "the broker password",
            ["SubjectDiscoveryPrefix"] = "the discovery prefix",
            ["SubjectTest"]            = "Test connection",

            // Why a typed device id is unusable. Composed where the id is validated, which is a plain
            // net10.0 type with no resource system of its own, so it reads them through this table.
            ["DeviceIdTooLong"]   = "An id can be at most {0} characters.",
            ["DeviceIdNoAlnum"]   = "An id must contain at least one letter or digit.",
        };
}
