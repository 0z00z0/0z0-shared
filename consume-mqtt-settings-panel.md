# Consuming MqttSettingsPanel

A checklist for an application adding the shared MQTT settings page. The panel renders the
structure; the host supplies the content and receives every edit as a callback.

## Reference

`ZeroZero.Mqtt.WinUI` (`net10.0-windows10.0.26100.0`), which pulls in `ZeroZero.Mqtt` and
`ZeroZero.Brand.WinUI` transitively. Same reference recipe as the About control — a
`ProjectReference` on a sibling checkout, carrying
`<UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>`.

`CommunityToolkit.WinUI.Controls.SettingsControls` is held at or below `8.2.251219`: a consuming app
holds a direct reference at that version, and a direct reference below a transitive one is NU1605,
an error. Raise both or neither.

## Host it

The panel is a tall `StackPanel` and scrolls nothing itself, so it goes inside the page's own
`ScrollViewer`. The section heading above it stays with the application.

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

Call `Initialise(MqttPanelSetup)` once, from the hosting page's constructor or `Loaded` handler.

## The setup object

Required: the settings store, the declared publish groups, the topic root, the activity record, an
accessor for the connection state, the publish-now callback, and the two change callbacks.

Optional and worth supplying: `RecallEndpoint` (the same accessor the connection is given, so the
Status rows can say what the connection landed on), `DefaultDeviceName`, the master card's title,
description and info text, `PublishGroupsInfo`, `DeviceIdConsequence`, `CommandLabel`, and `Log`.

Two obligations the panel's own copy depends on:

- **`ConnectionChanged` must run the connection's apply path.** The device-id dialogue promises the
  old entities are removed, and the discovery ledger is what keeps that promise.
- **`PublishSetChanged` must republish the discovery document**, not merely the state. The announced
  entity set is baked into the retained document.

## Declare the publish groups

`PublishGroup` carries a key, a label, an optional card description, a default state and an optional
info-icon line. The description and the info line are not two lengths of the same sentence: one
justifies the shipped default, the other says what the group contains. A group with no info text
gets no icon.

State persists per group key, never per index, so inserting or reordering a group cannot move a
user's choices onto different groups. A declared group renders whether or not it currently has
entities.

## Lifecycle

| Call | When |
|---|---|
| `Initialise(setup)` | Once, on the UI thread. |
| `Reload()` | Whenever the host re-shows the page. Keeps whatever is being typed. |
| `Refresh()` | When the page comes back on screen with nothing edited. |
| `Revert()` | Only behind an explicit control: it discards staged broker edits. |
| `Cancel()` | On window close, and when navigating away — an in-flight probe outlives the window. |

## Theming

Five keys, declared in the application's own resources to override: `MqttPanelHeadingBrush`,
`MqttPanelBodyBrush`, `MqttPanelSecondaryBrush`, `MqttPanelAccentBrush`,
`MqttPanelCardBackgroundBrush`. Keys left alone keep the stock WinUI theme. The module's defaults
are merged into `Application.Resources.MergedDictionaries`, so a key declared directly in
`Application.Resources` wins.

## Translation

One `.resw` under `Strings\en-GB\`, read through `ResourceLoader`. A consumer localises by adding a
language folder; a host with its own resource system supplies an `IMqttStringSource` on the setup
object instead. Everything falls back to the module's built-in en-GB, so a resource map that fails
to load leaves a readable panel rather than blank controls.

## What the panel never does

- It never writes a settings store directly — every commit goes through `IMqttSettingsStore.Update`,
  so a host whose configuration is one document keeps its own read-modify-write.
- **Test connection commits nothing at all**: not the fields, and not where the broker answered.
- Nothing in the Broker group takes effect until Apply, and an unapplied edit is marked beside the
  section heading rather than inside the group that holds the fields.
