# Consuming MqttSettingsPanel

A checklist for an application adding the shared MQTT settings page. The panel renders the
structure; the host supplies the content and receives every edit as a callback.

## Reference

`ZeroZero.Mqtt.WinUI` (`net10.0-windows10.0.26100.0`), which pulls in `ZeroZero.Mqtt`,
`ZeroZero.Mqtt.Discovery`, `ZeroZero.Config` and `ZeroZero.Brand.WinUI` transitively — the one
reference is the whole module, entity table included. Same reference recipe as the About control — a
`ProjectReference` on a sibling checkout, carrying
`<UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>`.

`CommunityToolkit.WinUI.Controls.SettingsControls` is held at or below `8.2.251219`: a consuming app
holds a direct reference at that version, and a direct reference below a transitive one is NU1605,
an error. Raise both or neither.

An app that ships its own language-folder resources declares `<DefaultLanguage>en-GB</DefaultLanguage>`
in its own project. The panel declares it for its own `.resw`, but a merged app PRI is built from the
app's resources and no library can declare the default on its behalf — without it MakePRI compares
them against `en-US` and warns `PRI257`.

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
| `Initialise(setup)` | Once, on the UI thread, before anything else. |
| `Reload()` | Whenever the host re-shows the page. Keeps whatever is being typed. Throws before `Initialise`. |
| `Refresh()` | When the page comes back on screen with nothing edited. |
| `Revert()` | Only behind an explicit control: it discards staged broker edits. Throws before `Initialise`. |
| `Cancel()` | On window close, and when navigating away — an in-flight probe outlives the window. |

`Reload` and `Revert` read the settings store the panel is handed on `Initialise`, so both throw an
`InvalidOperationException` naming the ordering when it has not been called. `Refresh` and `Cancel`
touch no store and stay silent.

## Theming

Six keys, declared in the application's own resources to override: `MqttPanelHeadingBrush`,
`MqttPanelBodyBrush`, `MqttPanelSecondaryBrush`, `MqttPanelAccentBrush`,
`MqttPanelCardBackgroundBrush` and `MqttPanelFontFamily`. Keys left alone keep the stock WinUI
theme.

**Declare them as immediate children of `Application.Resources`, and nowhere deeper.** The module
merges its defaults into `Application.Resources.MergedDictionaries` when the first panel is
constructed, and a dictionary's own entries outrank everything it merges.

```xml
<Application.Resources>
    <ResourceDictionary>

        <ResourceDictionary.MergedDictionaries>
            <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/>
            <!-- The application's own per-theme colours live here, keyed by its own names. -->
            <ResourceDictionary Source="ms-appx:///Themes/BrandColours.xaml"/>
        </ResourceDictionary.MergedDictionaries>

        <!-- The panel's six keys, directly in Application.Resources. -->
        <SolidColorBrush x:Key="MqttPanelHeadingBrush"        Color="{ThemeResource BrandHeadingColour}"/>
        <SolidColorBrush x:Key="MqttPanelBodyBrush"           Color="{ThemeResource BrandBodyColour}"/>
        <SolidColorBrush x:Key="MqttPanelSecondaryBrush"      Color="{ThemeResource BrandSecondaryColour}"/>
        <SolidColorBrush x:Key="MqttPanelAccentBrush"         Color="{ThemeResource BrandAccentColour}"/>
        <SolidColorBrush x:Key="MqttPanelCardBackgroundBrush" Color="{ThemeResource BrandCardColour}"/>
        <FontFamily x:Key="MqttPanelFontFamily">Segoe UI Variable Text</FontFamily>

    </ResourceDictionary>
</Application.Resources>
```

The theme variation belongs to the colour each brush resolves, not to where the brush is declared —
which is what keeps the keys at the one level the panel reads while light and dark still differ.

**Two placements silently do nothing.** Inside `Application.Resources.ThemeDictionaries` is one
level below what the panel resolves. Inside a dictionary the application merges itself is merged
before the module's own and loses to it. Either way **all six keys fall back to stock**, with no
warning and no build error, and the panel looks as though it ignores the host's theme entirely.

**Measure after overriding**, in both themes: sample the panel's heading and the application's own
heading in the same frame — an override that arrived makes them identical, byte for byte — and
compute the contrast of the panel's text against its ground, where below about 4.5:1 fails. A key
that fell back reads as the module's own default, which is exactly what the measurement separates
from a palette that is merely poor.

`MqttPanelFontFamily` is the only route to the panel's typeface: every element the panel styles
carries an explicit style, and an explicit style means a host's implicit `TextBlock` style is never
applied. A toolkit `SettingsCard`'s own header keeps the stock face — the toolkit's style carries the
family — and everything else on the panel follows the key.

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
