# The MQTT module

Four assemblies that put a Windows desktop application on an MQTT broker and, above that, into a
discovery-aware receiver as one device with entities. `ZeroZero.Config` stores settings.
`ZeroZero.Mqtt` speaks the protocol. `ZeroZero.Mqtt.Discovery` adds the entity and document layer.
`ZeroZero.Mqtt.WinUI` is the settings panel a host embeds.

This document is the implementation guide. It states what the module does and what an application
must supply; the rationale behind a given rule lives in the source comment beside it.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Broker | MQTT 5.0. The version is pinned, because a 5.0 CONNACK separates bad credentials from any other refusal and a 5.0 PUBACK says whether a publish was taken. |
| Receiver | Home Assistant 2024.11.0 or later. Device-based discovery sets that floor: an older receiver never subscribes to the device topic and sees no entities at all. |
| Panel | Windows 10 1809 (build 10.0.17763) or later, with the Windows App SDK. |

## The four assemblies

| Assembly | Target | References | Knows about |
|---|---|---|---|
| `ZeroZero.Config` | `net10.0` | — | Atomic JSON files, snapshot reads, mutation under one lock, quarantine of an unreadable file |
| `ZeroZero.Mqtt` | `net10.0` | `ZeroZero.Config`, `MQTTnet` | Topics, payloads, QoS, retain, the Last Will, transports, endpoint search, certificate trust, command routing, publish groups |
| `ZeroZero.Mqtt.Discovery` | `net10.0` | `ZeroZero.Mqtt`, `ZeroZero.Config` | Entities, component types, the device document, availability, eviction |
| `ZeroZero.Mqtt.WinUI` | `net10.0-windows10.0.26100.0` | `ZeroZero.Mqtt`, `ZeroZero.Brand.WinUI` | Rendering the settings a user edits |

The dependency runs one way. `ZeroZero.Mqtt` contains no entity vocabulary and no receiver
vocabulary beyond the default discovery prefix; a console tool or a service references it alone and
gets a broker connection with nothing above it. `ZeroZero.Mqtt.Discovery` is WinUI-free, so an
entity table composes in a plain `net10.0` test project with no broker and no UI present.

**A consumer takes one project reference.** Referencing `ZeroZero.Mqtt.Discovery` brings the core
and the settings assembly transitively; referencing `ZeroZero.Mqtt.WinUI` brings the panel as well.
A headless or test consumer references `ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` directly and
never pulls WinUI in — which is what keeps the entity model testable on a machine with no desktop.

`MQTTnet` is a functional package reference, present only in `ZeroZero.Mqtt`, and no client-library
type reaches a public signature: the module's own `MqttQos`, `MqttMessage`, `MqttConnackCode` and
`MqttPubackCode` stand in front of it.

---

## Wiring, in six steps

### 1. Take the reference

There is no NuGet feed, so a consumer takes a `ProjectReference` on a sibling checkout, routed
through an MSBuild property so CI can point it elsewhere without editing the `.csproj`:

```xml
<PropertyGroup>
  <ZeroZeroSharedDir Condition="'$(ZeroZeroSharedDir)' == ''">..\0z0-shared</ZeroZeroSharedDir>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="$(ZeroZeroSharedDir)\src\ZeroZero.Mqtt.WinUI\ZeroZero.Mqtt.WinUI.csproj">
    <UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>
  </ProjectReference>
</ItemGroup>
```

`UndefineProperties` is required whenever the consuming app publishes self-contained with
`WindowsAppSDKSelfContained` set as a global property: MSBuild propagates a global property into
every project reference, and the Windows App SDK targets reject it on a class library.

`CommunityToolkit.WinUI.Controls.SettingsControls` is a ceiling, not a preference. The panel holds
it at or below `8.2.251219`, because a consuming app holds a direct reference at that version and a
direct reference below a transitive one is NU1605, an error. Raise both or neither.

### 2. Open the settings store

`IMqttSettingsStore` is the module's entire storage dependency — three members, and no assumption
that the module owns the file behind them:

```csharp
MqttSettings Read();
void Update(Action<MqttSettings> mutate);
event Action? Changed;
```

`Update` is read-modify-write against the live state, so a caller holding a stale snapshot — a
settings panel opened some time ago — commits one field without rolling back what a sibling changed
meanwhile. `Changed` promises nothing about ordering, thread affinity or coalescing, and a
subscriber that does real work must assume all three.

`MqttSettingsFile` is the ready-made implementation over one JSON file. The module owns the file
name (`mqtt.json`); the host owns the directory:

```csharp
var settings = MqttSettingsFile.In(appDataDirectory);
```

A host whose configuration is one document with several unrelated sections implements the three
members over that document instead and never constructs `MqttSettingsFile`. Nothing else in the
module changes.

The same shape applies to the discovery ledger — `IDiscoveryLedgerStore`, two members, with
`DiscoveryLedgerFile.In(directory)` as the file-backed form, writing `mqtt-discovery.json` beside
the settings rather than inside them. See [The ledger](#the-ledger).

### 3. Declare the publish groups

A publish group is a user-facing switch over part of the published surface. The module declares
none and knows the vocabulary of none:

```csharp
var groups = new PublishGroupSet(settings,
[
    new PublishGroup("state", "State",
        Info: "What the machine is doing right now, sampled as it changes."),
    new PublishGroup("metrics", "Metrics",
        Description: "Off by default: these describe the application, not the hardware.",
        DefaultOn: false,
        Info: "Version, uptime and the application's own health counters."),
    new PublishGroup("controls", "Controls"),
]);
```

`Description` and `Info` are not two lengths of one sentence: the description justifies the shipped
default and sits under the row's label, while the info line says what the group contains and sits
behind an icon. A group with no info text gets no icon at all.

State persists per group key, never per index, so inserting or reordering a group cannot move a
user's choices onto different groups. A key absent from the stored dictionary takes the group's own
`DefaultOn`, so a group added in a later version starts where its author intended. A declared group
renders whether or not it currently has entities.

### 4. Declare the entity table

One declaration owns an entity whole: what the receiver needs to create it, where its state comes
from, and what an inbound payload does.

```csharp
var entities = new MqttEntitySet(
[
    new MqttSensor
    {
        EntityId    = "cpu_load",
        Name        = "CPU load",
        Group       = "metrics",
        Unit        = "%",
        StateClass  = MqttStateClass.Measurement,
        Read        = () => MqttPayload.Number(machine.CpuLoad),
    },

    new MqttBinarySensor
    {
        EntityId    = "on_battery",
        Name        = "On battery",
        DeviceClass = "battery",
        Group       = "state",
        Read        = () => machine.OnBattery,
    },

    new MqttSwitch
    {
        EntityId = "quiet_mode",
        Name     = "Quiet mode",
        Group    = "controls",
        Read     = () => machine.QuietMode,
        Apply    = on => MqttCommandVerdict.Accept(ct => machine.SetQuietModeAsync(on, ct)),
    },

    new MqttNumber
    {
        EntityId = "fan_target",
        Name     = "Fan target",
        Group    = "controls",
        Min      = 20,
        Max      = 100,
        Step     = 5,
        Unit     = "%",
        Read     = () => machine.FanTarget,
        Apply    = value => machine.CanSetFan
            ? MqttCommandVerdict.Accept(ct => machine.SetFanAsync(value, ct))
            : MqttCommandVerdict.Refuse("The fan is under firmware control right now."),
    },

    new MqttSelect
    {
        EntityId = "profile",
        Name     = "Profile",
        Group    = "controls",
        Include  = () => machine.Profiles.Count > 0,
        Options  = () => machine.Profiles,
        Read     = () => machine.Profile,
        Apply    = option => MqttCommandVerdict.Accept(ct => machine.SelectProfileAsync(option, ct)),
    },

    new MqttButton
    {
        EntityId = "restart",
        Name     = "Restart",
        Group    = "controls",
        Press    = () => MqttCommandVerdict.Accept(ct => machine.RestartAsync(ct)),
    },
]);
```

The set rejects a duplicate entity id and an id that is not topic-safe, at construction. A shared id
is one `unique_id`, so the second entity would replace the first in the receiver's registry and take
the first's commands with it.

An id composed from a runtime name goes through `MqttEntityIdAllocator`, which normalises to the
topic-safe alphabet and resolves the collisions such names produce:

```csharp
var ids = new MqttEntityIdAllocator();
var perMachine = machine.Volumes.Select(v => new MqttSensor
{
    EntityId = ids.Allocate($"volume_{v.Label}_free"),
    Name     = $"{v.Label} free space",
    ...
});
```

`MqttEntityId.Resolve(names)` does the same for a whole list at once. Order matters and is the
input's, so the same list always produces the same ids.

### 5. Compose the connection

The connection knows nothing of discovery. The publisher hangs on it as an
`IMqttConnectionListener`, and the two are tied to each other:

```csharp
MqttConnection? connection = null;

var publisher = new DiscoveryPublisher(new DiscoveryPublisherSetup
{
    IsConnected       = () => connection?.IsConnected ?? false,
    TopicRoot         = "exampleapp",
    Device            = new DiscoveryDevice("Example Vendor", "Example App", "1.4.0",
                            ConfigurationUrl: "https://example.invalid/app"),
    Origin            = new DiscoveryOrigin("Example App", "1.4.0",
                            SupportUrl: "https://example.invalid/support"),
    Entities          = entities,
    Ledger            = DiscoveryLedgerFile.In(appDataDirectory),
    Groups            = groups,
    Retired           = retiredEntities,
    SetChannelsAsync  = (channels, ct) => connection!.SetChannelsAsync(channels, ct),
    SetCommandTargets = targets => connection!.SetCommandTargets(targets),
    Log               = log,
});

connection = new MqttConnection(new MqttConnectionSetup
{
    TopicRoot         = "exampleapp",
    Channels          = publisher.Channels(),
    CommandTargets    = publisher.CommandTargets(),
    Subscriptions     = [publisher.BirthMessage(settings.Read().DiscoveryPrefix)],
    Listener          = publisher,
    DefaultDeviceName = machineName => $"Example App ({machineName})",
    RecallEndpoint    = () => endpointMemory,
    RememberEndpoint  = memory => endpointMemory = memory,
    CommandRefused    = refusal => log.Info($"MQTT: {refusal.EntityId} refused ({refusal.Outcome})."),
    Log               = log,
});

connection.Apply(settings.Read().Connect());
settings.Changed += () => connection.Apply(settings.Read().Connect());
```

**The topic root is the application's own segment** at the head of every state and command topic,
and it is the stem of the default device id. The same value goes to both setups. Nothing in the
module names a product.

**`Apply` is idempotent**, and does nothing when handed parameters it already holds. That is what
makes applying on every settings change safe: a group toggle and a successful connect both leave
the projection identical, so neither bounces the socket.

**Publishing a value** is a signal, not a push. The channel's payload function is read on a
background thread, debounced and coalesced:

```csharp
connection.RequestPublish("cpu_load");   // one channel
connection.RequestPublish();             // every declared channel
connection.Publish("cpu_load", "37");    // a payload the caller already has
await connection.PublishNowAsync();      // every channel, dedupe bypassed
```

An unchanged payload is cached but not sent. `PublishNowAsync` bypasses that on purpose — it is
what a user-pressed "Publish now" needs, where nothing leaving the machine is indistinguishable
from a dead connection.

**Three host obligations the module cannot meet itself:**

- Call `connection.OnPowerResume()` from the host's power-mode handler. The connection does not
  subscribe to system events, because the unsubscribe lifetime belongs to the host.
- Call `connection.Dispose()` on exit. Teardown is synchronous, bounded and idempotent, and
  publishes offline before the socket goes.
- Call `connection.SetSubscriptions(...)` again after a discovery-prefix change if the birth
  message is wired, since the filter carries the prefix. Subscriptions take effect at the next
  connect, which a prefix change causes anyway. A stored prefix that is blank means the default,
  so compose the filter from `MqttSettings.DefaultDiscoveryPrefix` when the stored value is empty.

**A table that changes at runtime** is replaced whole, never mutated:

```csharp
await publisher.SetEntitiesAsync(new MqttEntitySet(rebuilt));
```

That rebuilds the channels, the command targets and the document in one pass, and empties the state
topics of entities that have gone.

### 6. Host the panel

The panel is a tall `StackPanel` and scrolls nothing itself, so it goes inside the page's own
`ScrollViewer`. The section heading above it stays with the application:

```xml
<Page ... xmlns:mqtt="using:ZeroZero.Mqtt.WinUI">
    <ScrollViewer>
        <StackPanel Spacing="12">
            <TextBlock Text="MQTT" Style="{StaticResource SectionHeaderStyle}"/>
            <mqtt:MqttSettingsPanel x:Name="Mqtt"/>
        </StackPanel>
    </ScrollViewer>
</Page>
```

```csharp
Mqtt.Initialise(new MqttPanelSetup
{
    Settings            = settings,
    Groups              = groups,
    TopicRoot           = "exampleapp",
    Activity            = connection.Activity,
    ConnectionState     = () => connection.State,
    PublishNow          = () => connection.PublishNowAsync(),
    ConnectionChanged   = () => connection.Apply(settings.Read().Connect()),
    PublishSetChanged   = () => publisher.Republish(),
    RecallEndpoint      = () => endpointMemory,
    DefaultDeviceName   = $"Example App ({Environment.MachineName})",
    PublishTitle        = "Publish to MQTT",
    PublishDescription  = "Publishes this application's state to an MQTT broker.",
    PublishGroupsInfo   = "Each group is a set of entities. Switching one off stops it publishing "
                        + "and marks its entities unavailable; nothing is deleted.",
    DeviceIdConsequence = "Any dashboard card pointing at the old entities has to be repointed.",
    CommandLabel        = id => entities.NameOf(id),
    Log                 = log,
});
```

`Initialise` is called once, on the UI thread, from the hosting page's constructor or its `Loaded`
handler.

**Two obligations the panel's own copy depends on.** `ConnectionChanged` must run the connection's
apply path, because the device-id dialogue promises the old entities are removed and the ledger is
what keeps that promise. `PublishSetChanged` must republish the discovery document, not merely the
state, because the announced entity set is baked into the retained document.

| Call | When |
|---|---|
| `Initialise(setup)` | Once, on the UI thread. |
| `Reload()` | Whenever the host re-shows the page. Keeps whatever is being typed. |
| `Refresh()` | When the page comes back on screen with nothing edited. |
| `Revert()` | Only behind an explicit control: it discards staged broker edits. |
| `Cancel()` | On window close, and when navigating away — an in-flight probe outlives the window. |

`BrokerExpanded` and `PublishExpanded` read and write the two expanders, so a host restores the
section a user left open.

---

## What the host supplies, and what it never sees

The host supplies the topic root, the device and origin blocks, the entity table, the publish
groups, the settings store, the ledger store, the log sink, the endpoint-memory callbacks, and the
panel's application-shaped copy.

The module never sees a product name, a domain concept, a payload shape or a logging framework.
Concretely, three things stay out of anything the module holds:

- **The password is fetched, never held.** `MqttConnectParameters.Password` is a `Func<string>`
  excluded from equality; `CredentialRef` — a short non-reversible fingerprint — is what says the
  secret changed. Nothing compared, logged or passed between threads carries the secret.
- **The group state is absent from the connect parameters**, so a group toggle republishes and
  never bounces the socket.
- **Endpoint memory is absent too.** It reaches the host through `RememberEndpoint` and comes back
  through `RecallEndpoint`, so a successful connect is not a settings change — which would
  otherwise make a consumer that re-applies on a settings change reconnect on the strength of its
  own success.

Two projections built from equal settings are equal, which is the whole of why applying on every
settings change is safe.

---

## The entity model

Seven typed component types. The hierarchy is closed to the assembly: the seven are the receiver's
whole vocabulary, so an eighth is a change here rather than in a consumer.

| Class | Platform | Reader | Writable | Absent reading |
|---|---|---|---|---|
| `MqttSensor` | `sensor` | `Func<string?>` | no | `None` |
| `MqttBinarySensor` | `binary_sensor` | `Func<bool?>` | no | `None` |
| `MqttSwitch` | `switch` | `Func<bool?>` | `Apply(bool)` | `None` |
| `MqttButton` | `button` | none | `Press()` | n/a — no state topic |
| `MqttNumber` | `number` | `Func<double?>` | `Apply(double)` | `None` |
| `MqttSelect` | `select` | `Func<string?>` | `Apply(string)` | `None` |
| `MqttText` | `text` | `Func<string?>` | `Apply(string)` | empty topic |

Every reader is required, and typed to what its platform holds — a boolean for a switch, a double
for a number. Only the sensor's is a string, because a sensor carries a number, a duration or a word
with equal standing. Numeric payloads go through `MqttPayload.Number`, which formats with
`InvariantCulture`: a decimal comma on the wire is read as a thousands separator by a receiver in
another locale, or not at all.

**One bare topic per entity, carrying a plain value.** Nothing composes a JSON payload, nothing
writes a `value_template`, and a shell script or a flow engine reads a topic with no parsing.

Members common to every entity:

| Member | Effect |
|---|---|
| `EntityId` (required) | The `unique_id` stem, the state topic's last segment and the command topic's. Must already be topic-safe. |
| `Name` (required) | What the receiver shows. **Null makes the entity the device's main feature** — the receiver then names it after the device alone. One per device. |
| `Group` | The publish-group key, or null for an entity that is always published. |
| `Category` | `Primary` writes no `entity_category` and keeps the entity on the main card; `Config` and `Diagnostic` file it behind the matching fold. |
| `Icon`, `DeviceClass` | The receiver's own vocabulary. Open sets, so strings rather than enums that would go stale. |
| `Include` | Capability gating, evaluated on every announcement pass. Null means true. |
| `Debounce` | How long a requested publish waits before reading, so a burst collapses into one read. `MqttConnection.ReflectDebounce` is the 250 ms constant for a value signalled by something that has just written to it. |
| `EnabledByDefault` | Whether the receiver enables the entity when it first appears. |
| `Retain` | Whether the state topic is published retained. Set it false on anything declaring `ExpireAfter`: a retained value is replayed on every subscribe, so an expiry that already elapsed comes back looking current. |
| `Extra` | Discovery keys this model has no property for, merged into the component entry last. The escape hatch, and deliberately small. |

**`Include` may fail as well as answer.** It runs on the announcement thread and usually reads live
hardware, so a throw is not a false: it says the capability could not be read, which is not the same
as absent. An entity whose predicate throws keeps whatever the record already says about it, and one
unanswered read cannot rewrite the document.

Members each component adds of its own:

| Class | Adds |
|---|---|
| `MqttSensor` | `Unit`, `StateClass`, `DisplayPrecision`, `ForceUpdate`, `ExpireAfter` |
| `MqttBinarySensor` | `PayloadOn`, `PayloadOff`, `ForceUpdate`, `ExpireAfter` |
| `MqttSwitch` | `PayloadOn`, `PayloadOff` |
| `MqttButton` | `PayloadPress`, defaulting to `MqttButton.DefaultPress` (`PRESS`) |
| `MqttNumber` | `Min` and `Max` (both required), `Step`, `Unit`, `Mode` |
| `MqttSelect` | — |
| `MqttText` | `MinLength`, `MaxLength` (ceiling `MqttText.MaxLengthCeiling`, 255), `Mode`, `Pattern` |

Bounds are declared once and enforced twice: the receiver keeps its own control inside them, and
`Accept` refuses anything outside them, because a payload can arrive from anything holding a broker
connection. A `MqttNumber` step below `MqttNumber.MinimumStep` (0.001) is refused at declaration —
the receiver's schema rejects it, and the component would vanish from the document with nothing to
see locally. `MqttText.Pattern` is never the only guard; `Accept` still judges what arrives.

### Topics

| Topic | Shape |
|---|---|
| State | `<topicRoot>/<deviceId>/<entityId>` |
| Command | `<topicRoot>/<deviceId>/cmd/<entityId>` |
| Availability | `<topicRoot>/<deviceId>/availability` |
| Withheld availability | `<topicRoot>/<deviceId>/availability/withheld` |
| Device document | `<discoveryPrefix>/device/<deviceId>/config` |
| Single-component config | `<discoveryPrefix>/<component>/<deviceId>/<entityId>/config` |
| Receiver birth | `<discoveryPrefix>/status` |

`MqttTopics` composes the first four and `DiscoveryTopics` the rest; nothing assembles a topic
string anywhere else. One wildcard subscription — `<topicRoot>/<deviceId>/cmd/#` — covers every
command entity, and the router resolves by entity id.

The module publishes no single-component config. It empties those paths for a declared
`RetiredEntity` and hands them over for a declared `MigratingEntity`.

The device id defaults to `<topicRoot>_<sanitised machine name>`, and it must be unique across every
installation publishing to one broker: it is the MQTT client id — two machines sharing it disconnect
each other in a loop — and the `unique_id` stem, so they would also overwrite each other's entities.
Nothing local can check that, so a host offering the field says so where the user types it. The
machine-name default is unique by construction.

### Commands

`MqttCommandEntity.Accept(payload)` is the domain seam: parse the payload, validate it against the
application's own bounds, and return either a refusal carrying a reason or the work to run. The
component parses as far as its own type goes, so no consumer parses a payload twice.

```csharp
MqttCommandVerdict.Accept(ct => machine.SetQuietModeAsync(on, ct))   // work to run
MqttCommandVerdict.Accept(() => machine.QuietMode = on)              // nothing to await
MqttCommandVerdict.Refuse("The fan is under firmware control right now.")
MqttCommandVerdict.OutOfRange("Expected 20 to 100.")
MqttCommandVerdict.NotAnOption("'Turbo' is not one of the current options.")
MqttCommandVerdict.Malformed("Expected a number.")
```

Everything but `Accepted` publishes nothing, changes nothing and clamps nothing — a refusal is a
refusal, not a correction. `Accept` runs on the receive callback and only decides; the work it
carries runs on a single-reader command worker, so one command's read-modify-write finishes before
the next starts.

The module composes no refusal sentence of its own. `MqttConnectionSetup.CommandRefused` receives
the facts and the entity's own wording verbatim, because only the application knows why a value it
understands is one it will not act on.

Two refusals arise below the entity. A payload for an entity that is not currently announced is
`Unrecognised`, so a command addressed to a switched-off group is reported rather than quietly acted
on. A payload that arrives with the retain flag set is `Retained` and the topic is emptied: a
command is an event, and a retained one would be redelivered and re-fire on every reconnect.

### The document

One retained payload describes the whole device. The device block, the origin block and the
availability keys are written once at the root; a set of several dozen entities is announced in a
single retained publish.

Root keys are `dev`, `o`, `availability_topic`, `payload_available`, `payload_not_available`, `qos`
and `cmps`. Each entry under `cmps` is keyed by entity id and carries `p` (the platform),
`unique_id`, `name`, its topics, and whatever the declaration filled in.

**A component is removed by writing it with only its platform key.** Leaving it out of a later
document does not remove it — the receiver keeps what it already has — so removal is something the
document says, not something it omits.

---

## Identity, and what it guarantees

**An entity's `unique_id` carries it across any change to its topics or to the discovery format.**
It is composed as `<deviceId>_<entityId>` and is the only identity in the document: there is
deliberately no `object_id` and no `default_entity_id`, both of which pin an entity id the receiver
is better left to compose.

A removed entity is not discarded by the receiver. Its record is kept with its entity id, name,
icon, area, labels, aliases, flags **and its registry id**, and re-announcing the same `unique_id`
restores all of it.

**This means every existing reference still resolves** — including an automation that references an
entity by registry id rather than by entity id. That is a stronger promise than "the entity comes
back with its name", and it is the one that matters when deciding whether a change is safe. The
records are serialised, so they outlive a receiver restart, and while the MQTT integration exists a
deleted MQTT entity is never purged.

The consequence for a consumer: **an entity may be moved between component types, renamed, regrouped
or moved to a different topic without a user losing anything they set on it, provided its
`unique_id` is unchanged.** The device id is the other half of that id, so a change to the device id
is a different device and renames every entity at once.

### The one asymmetric field

**The entity id is restored only if it is still free.** If anything claims it while the entity is
absent, the return gets a generated id and the user's chosen one is gone permanently — there is no
recovery.

That is the reason not to remove entities needlessly, and it is why a reversible state is announced
as unavailable rather than as a removal. Nothing in the module tries to rescue an entity id; the
design simply avoids creating the gap in which one can be taken.

---

## Why an entity stopped being published

Four intents, four behaviours. The ledger records which, so a restart replays none of them as
another.

| Intent | Reached by | On the wire | Reversible |
|---|---|---|---|
| **Deleted** | The entity table no longer contains it | Written into the document with only its platform key, which removes it; its retained state topic is emptied afterwards | Only by re-announcing the same `unique_id` |
| **Withheld** | Its group is switched off, or `Include` returns false | Kept in the document whole, pointed at `…/availability/withheld`, which permanently retains the offline payload | Yes, at no cost |
| **Migrating** | Declared `MigratingEntity` | The single-component config gets `{"migrate_discovery":true}` before the document, and is emptied after it | n/a — a one-way handover, recorded once |
| **Retired** | Declared `RetiredEntity` | The single-component config is emptied once, and the fact is written down | n/a — permanent by declaration |

**Withheld is availability, never removal.** A group toggle is a settings checkbox that commits on
the spot, and a capability predicate goes false whenever hardware goes away; announcing either as a
deletion would churn the receiver's registry for a state that reverses in a second. A withheld
entity keeps its whole entry, stays on the device's own page, and shows unavailable — its
availability genuinely independent of the device's, because the component's own
`availability_topic` overrides the root's.

Only an entity the record says was already announced is held unavailable. One whose group has never
been switched on has no registry entry to protect, and announcing it would create the very thing the
user declined.

**Removing the device outright is a separate, explicit operation** — `MqttConnection.RemoveDeviceAsync`
or `DiscoveryPublisher.RemoveDeviceAsync`. It empties the document, both availability topics, every
state topic and every command topic, and it deletes every registry entry the device owns along with
the names, entity ids and areas the user chose. Nothing a user does to the settings can reach it.
Switching publishing off, or blanking the host so the configuration stops being complete, publishes
offline and leaves everything standing.

### The ledger

Diffing a new entity set against the previous one held in memory is correct only for a fixed table.
An entity removed while the application was closed is never diffed at all: nothing on the next run
knows it existed, and its retained config and state topics stay on the broker for ever. A per-machine
entity set — one entry per virtual machine, per drive, per adapter — reaches that case on the first
removal.

So what was published is written down, per identity, as composed topics rather than as their parts:
what has to be emptied is exactly what was sent, and recomposing it under today's rules would empty
a topic nothing was ever published on. The next connect reconciles against the record.

`Ledger` is required on `DiscoveryPublisherSetup` and has no default, because every alternative is a
choice with consequences. `DiscoveryLedgerFile.In(directory)` is one line and durable.
`TransientLedgerStore` is the deliberate opt-out — right for a test and for a host with genuinely
nowhere to write — and without a durable store an entity removed while the application was closed is
never evicted, a retirement is replayed on every start, and a migration is replayed as a retirement,
which removes what the handover kept.

A pass writes the record only once every message in it has reached the broker. A half-landed pass
leaves the record as it was, so the next connect evicts what this one failed to.

---

## Measured behaviour that contradicts the obvious implementation

Each of these was measured against a live receiver, and each is what an implementation written from
first principles gets wrong.

**An empty payload does not clear a state.** On `sensor`, `binary_sensor`, `switch`, `number` and
`select` the receiver ignores a zero-length payload and goes on showing the last value it saw. The
stale value stands, indefinitely. The literal `None` — `MqttPayload.None` — is what clears them, and
every one of those platforms publishes it for an absent reading. Text is the exception: an empty
string is a value there, so `MqttText` empties its topic and the two are indistinguishable on the
wire. A consumer that needs them apart declares a sentinel of its own through `Extra` and never
returns null.

`None` collides with a text-valued sensor whose genuine reading is the word `None`. That is
unavoidable: the receiver reserves the literal and offers no second form.

**Root-level availability is inherited by every component.** It is written once at the document root
and no component repeats it. The only component that carries its own is one being withheld, whose
override is the whole of how it reads unavailable without its registry entry being touched.

**One malformed component does not poison the document.** The receiver drops that component and
keeps the rest, so a schema mistake in one entity costs that entity and nothing else — which is why
`MqttNumber` refuses a step below `0.001` at declaration rather than letting the component vanish
with nothing to see locally.

**The receiver floor is 2024.11.0**, set by device-based discovery itself rather than by any option
in the document. An older receiver never subscribes to the device topic and sees nothing at all.

**Withholding an entity is reversible and costs nothing permanent.** A group toggle and a capability
predicate going false are both announced as availability, and the entity returns with every
customisation intact — name, entity id, icon, area, labels, aliases and registry id.

---

## The encryption model

Three modes, on `MqttSettings.EncryptionMode`:

| Mode | Behaviour |
|---|---|
| `On` | Every candidate is encrypted. Nothing falls back. |
| `Off` | Every candidate is plain, except a WebSocket port whose scheme is fixed by convention — 443, 8084 and 8883 resolve to `wss` by their address, which no setting can undo. |
| `Auto` | Each endpoint is tried encrypted first and then in clear text, subject to the classification below. |

**Encryption order is evaluated per endpoint, not per sweep.** The plain retry of one port is
attempted before the encrypted attempt on the next one, so an ordinary internal broker on 1883 is
reached on the second attempt rather than after the entire encrypted list.

Under `Auto`, whether the clear-text half of a pair is reached at all turns on a three-way
classification of what the encrypted attempt found:

| Encrypted attempt | Downgrade | Why |
|---|---|---|
| **Authentication refused** (`AuthRejected`) | **Blocked** | The broker answered and said no. The sweep ends there in any case; a wrong password never causes a retry in clear text. |
| **A certificate was presented and rejected** (`TlsUntrusted`) | **Blocked** | Encryption *was* available. A clear-text retry would put the password on the wire at the very broker that offered to take it in cipher. **Certificate trust is the resolution, not a downgrade.** |
| **No certificate was presented** (`TlsUnsupported`) | **Allowed** | The far end took the socket and never offered a certificate: nothing secure was on offer, and no credentials left the machine, because the handshake fails before CONNECT. This is the ordinary way an internal broker on 1883 is found. |

`Unreachable` — the operating system saying there is nothing there — also leaves the downgrade open,
for the same reason: nothing was sent. A timeout and an unclassified failure block it, because
neither says what was on offer.

The two TLS outcomes are separated by whether a certificate arrived during the handshake, recorded
as the handshake happens. They cannot be told apart from the exception: a broker with no TLS on its
port reads a ClientHello as a malformed packet and closes the socket, and a broker with an untrusted
certificate fails the handshake, and both reach the client as an ordinary communication failure.

**Certificate trust** is a setting rather than a hook, because encryption forced on against a broker
with a self-signed certificate cannot connect under system trust alone, and the failure otherwise
reads as "the connection failed" with no route to a fix:

```csharp
MqttCertificateTrust.SystemTrust                    // the platform's own stores
MqttCertificateTrust.ForThumbprint("A1 B2 C3 …")    // SHA-1, any spacing or case
MqttCertificateTrust.ForCertificate(base64OrCert)   // byte for byte
```

Pinning is exact rather than a blanket "accept anything": a link that accepts every certificate is
encrypted against a passive listener and open to an active one. An unusable pin refuses rather than
falling through to platform validation.

### The endpoint sweep

`MqttEndpointPlan` is pure — the staged settings, what last worked and the attempts so far go in, a
candidate or "nothing left to try" comes out. The live connection and the Test connection button
both walk it, so the button's verdict is about the connection that will actually be made.

TCP leads under `Auto` because it is the internal path and the cheaper one. Ports are `1883, 8883`
for TCP and `443, 9001, 8083, 8084, 8080, 80` for WebSocket — the WebSocket list is the front door's
ports rather than MQTT's, with 80 and 8080 last because a CDN accepts a socket on both whether or
not MQTT is behind them.

A remembered endpoint leads the sweep but is never the whole of it, so a machine that moves pays one
extra attempt rather than losing the connection. It is keyed on host and username together, because
the same broker legitimately answers differently from inside and outside a network, and commonly
fronts a separate listener per account. An explicit port, transport or encryption is honoured
exactly and is never reached around, remembered entry included.

A probe runs only on `BrokerSettingChanged`, `TestConnection` or `Apply`, and only while publishing
is enabled and a host is set. Showing a settings page is deliberately not a trigger: a probe costs
real seconds and puts the machine on the network.

---

## The panel

`MqttSettingsPanel` renders five sections: the master switch, a live Status block, the device
identity, a staged Broker block behind an Apply, and one row per declared publish group. It knows no
application's subject matter; everything domain-shaped arrives through `MqttPanelSetup` and every
edit reports back as a callback.

**Two commit models sit side by side.** The master switch, the device name and the group toggles
commit immediately. Everything in the Broker group is staged and takes effect on Apply, so the
connection is remade once per edit session rather than once per keystroke. An unapplied edit is
marked beside the section heading rather than inside the group holding the fields, so a collapsed
expander cannot hide one.

**Test connection commits nothing at all** — not the fields, and not where the broker answered. One
validation gate serves both Apply and Test, so a green test cannot vouch for a configuration Apply
would refuse.

The panel never writes a settings store directly: every commit goes through `IMqttSettingsStore.Update`,
so a host whose configuration is one document keeps its own read-modify-write.

### The info-text ownership split

**Protocol vocabulary is the module's.** What a transport is, what the discovery prefix controls,
what Automatic does about encryption, what the endpoint search remembers — identical for every
consumer, and no host writes any of it.

**What an application publishes is the host's.** The module knows none of it. Four members carry it,
and `PublishGroupsInfo` deliberately has no fallback, so an unset one leaves the heading with no
icon rather than an empty one:

| Member | Where it renders |
|---|---|
| `PublishTitle` | The master switch card's header |
| `PublishDescription` | The one line under the master switch — the only place the panel says what this application publishes |
| `PublishInfo` | The master switch's info icon |
| `PublishGroupsInfo` | The "What to publish" heading's icon. No fallback. |
| `DeviceIdConsequence` | An application-specific consequence, shown in the device-id dialogue under the module's own |

### Theming and translation

Five brush keys, declared in the application's own resources to override:
`MqttPanelHeadingBrush`, `MqttPanelBodyBrush`, `MqttPanelSecondaryBrush`, `MqttPanelAccentBrush`,
`MqttPanelCardBackgroundBrush`. Keys left alone keep the stock WinUI theme. The module's defaults
are merged into `Application.Resources.MergedDictionaries`, so a key declared directly in
`Application.Resources` wins.

Every user-facing string the module owns is in `MqttStrings`, keyed, with its en-GB text built in
and exposed as `MqttStrings.Builtin` for a consumer generating a translation template. A consumer
localises by adding a language folder alongside `Strings\en-GB\`, or by supplying an
`IMqttStringSource` on the setup object. A lookup that finds nothing answers null and the built-in
en-GB stands, so a resource map that fails to load leaves a readable panel rather than blank
controls.

`MqttPanelText` composes every sentence the panel renders, and `MqttStatusText` is its static facade
over the module's own en-GB — useful to a host rendering the same status outside the panel, in a
tray tooltip or a log line.

---

## Differences from an earlier shape

A consumer with code written against a per-component, shared-payload implementation meets the
following. Each is a compile error or a topic move, not a silent behaviour change.

**Assemblies and namespaces**

| Was | Is |
|---|---|
| `ZeroZero.Mqtt.HomeAssistant` | `ZeroZero.Mqtt.Discovery` |
| `Microsoft.Extensions.Logging` abstractions | `IMqttLog`, two members, with `NullMqttLog` as the default |
| — | `ZeroZero.Config` at the base of the graph |

**The entity model**

| Was | Is |
|---|---|
| `HaEntity`, `HaSensor`, `HaBinarySensor`, `HaSwitch`, `HaButton`, `HaNumber`, `HaSelect`, `HaText` | `MqttEntity`, `MqttSensor`, `MqttBinarySensor`, `MqttSwitch`, `MqttButton`, `MqttNumber`, `MqttSelect`, `MqttText` |
| `ObjectId` | `EntityId` |
| `Role` (`HaEntityRole`) | `Category` (`MqttEntityCategory`) |
| `UnitOfMeasurement` | `Unit` |
| `StateClass` as a string | `MqttStateClass` |
| `State` | `Read`, required on every component but the button |
| `Apply(value, ct)` returning a task | `Apply(value)` returning an `MqttCommandVerdict` that carries the work |
| `ValueTemplate` | removed — one bare topic per entity, and no templates anywhere |
| `Options` as a fixed list | `Func<IReadOnlyList<string>>`, read on every announcement pass |
| A writable entity with no state provider | not expressible — every component but the button has a required reader |
| `Include` defaulting to `() => true` | `Include` nullable, null meaning true, and a throw meaning "could not be read" |
| `HaEntitySet.Channels()` / `CommandTargets(logger)` | `MqttEntitySet.Channels(published)` / `CommandTargets(published)`, static; or `DiscoveryPublisher.Channels()` / `CommandTargets()` |
| `HaNode` | `MqttDeviceIdentity` plus `DiscoveryPublisher` |

**The wire**

| Was | Is |
|---|---|
| One retained config per component, at `<prefix>/<component>/<node>/<object>/config` | One retained device document, at `<prefix>/device/<deviceId>/config` |
| `object_id` and `default_entity_id` | neither — `unique_id` is the only identity |
| A shared payload plus `value_template` per entity | one bare topic per entity, carrying a plain value |
| A withheld entity's config topic emptied | the entity kept in the document, pointed at the withheld availability topic |
| An absent reading emptying the topic | `MqttPayload.None` on every platform but text |
| No record of what was published | `IDiscoveryLedgerStore`, required, with no default |

**The core**

| Was | Is |
|---|---|
| `MqttOptions` | `MqttSettings` (persisted) and `MqttConnectParameters` (projected, and carrying no password, group state or endpoint memory) |
| `MqttOptionsValidator` | `MqttIdentity`, `MqttEntityId` and `MqttBrokerEdits.Validate` |
| `IMqttCredentialStore` | removed — `MqttConnectParameters.Password` is a fetch delegate and `CredentialRef` a fingerprint |
| `MqttConnectionPlan` | `MqttEndpointPlan` |
| `MqttTransportSetting`, `MqttEncryptionSetting` | `MqttTransportMode`, `MqttEncryptionMode` |
| `UseTls` as a boolean | `MqttEncryptionMode`, three-valued, plus `MqttCertificateTrust` |
| `MqttDetectStage`, `MqttDetectProgress` | `MqttSearchStage`, `MqttSearchProgress` |
| `LastGoodEndpoint` stored in the options | `RecallEndpoint` and `RememberEndpoint` callbacks on the connection setup |
| `NodeId` | `DeviceId` |

**The panel**

| Was | Is |
|---|---|
| `MqttPanelOptions`, `MqttPanelSnapshot` | `MqttPanelSetup` |
| `MqttPublishCategory` | `PublishGroup`, declared on `PublishGroupSet` |
| `MqttSettingsDraft` | `MqttBrokerEdits` |
| `MqttPanelGates` | `MqttBrokerEdits.Validate`, one gate for Apply and Test |
| `MqttStatusFormatter` in the WinUI assembly | `MqttStatusText` and `MqttPanelText` in the core |
| Callbacks per edited field (`OnEnabledChanged`, `OnBrokerApplied`, `OnNodeIdChanged`, …) | `ConnectionChanged` and `PublishSetChanged` |

---

## Deliberately not in the module

The entity table, every payload shape, every option list, every bound a command is judged against,
the refusal wording, the logging framework, the credential store, the power-mode subscription, and
the settings document a host already owns. Each is either the application itself or a lifetime the
module cannot manage on the host's behalf.
