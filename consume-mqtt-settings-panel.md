# Consuming MqttSettingsPanel

A checklist for an application adding the shared MQTT settings page. The panel renders the
structure; the host supplies the content and receives every edit as a callback.

## Reference

`ZeroZero.Mqtt.WinUI` (`net10.0-windows10.0.26100.0`), which pulls in `ZeroZero.Mqtt`,
`ZeroZero.Mqtt.Discovery`, `ZeroZero.Config`, `ZeroZero.Brand.WinUI` and `ZeroZero.Brand.Core`
transitively — the one reference is the whole module, entity table and About control included. Same reference recipe as the About control: either a `PackageReference` on the studio's GitHub Packages
feed, which authenticates every read and so needs a token with `read:packages` plus a `nuget.config`
mapping `ZeroZero.*` to it, or a `ProjectReference` on a sibling checkout carrying
`<UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>`. Both are set out in
[the README](README.md#1-reference-the-library).

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

**If the application already stores MQTT settings of its own, carry them into the store before the
panel is first shown.** The module reads only its own file, so an upgrading user otherwise opens the
panel to an empty broker host with their existing configuration still sitting unread in the old
location — and a fresh install looks correct either way, so testing one proves nothing. See
[Carrying an existing broker block across](docs/zerozero-mqtt.md#carrying-an-existing-broker-block-across).

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

### What a host's branding reaches

**A host branding the panel gets the module's own surfaces and the toolkit's cards and expanders,
matching each other. What stays stock is the WinUI control parts.** Two override sets do it: the six
keys above for the panel's own surfaces, and the application's own override of the toolkit's
semantic brushes — `CardBackgroundFillColorDefaultBrush` and `TextFillColorPrimaryBrush` — for the
cards. A host declaring both already has the second, because it is what brands its other pages.

Measured on an installed, rendered build: the application's card override reached every toolkit
`SettingsCard` **including those inside the panel**, at exactly the studio ground — flat and opaque,
strokes matching at 2 px — while a `ComboBox` on the same page kept stock values until its own
control keys were declared. `MqttPanelCardBackgroundBrush` took the same application override, so the
Status band and the toolkit cards beside it measure identically.

**The decision is therefore not whether to brand the panel.** It is whether to accept stock control
chrome or to take on the control key sets, which for the controls this panel renders is 266 live
brush keys.

| Surface | What reaches it |
|---|---|
| Section headings | `MqttPanelHeadingBrush` |
| The Status block's ground | `MqttPanelCardBackgroundBrush` |
| The Status block's labels | `MqttPanelBodyBrush` |
| Row descriptions, hints, status values, the device-id echo | `MqttPanelSecondaryBrush` |
| The applied, unapplied and saved markers | `MqttPanelAccentBrush` |
| The typeface wherever the panel styles text, the value text inside the fields included | `MqttPanelFontFamily` |
| Every `SettingsCard` and `SettingsExpander` ground and header | The application's toolkit semantic brushes |
| The three `ComboBox`es, every `TextBox`, the `PasswordBox`, every `Button`, the `ToggleSwitch`, the info icons, the device-id dialogue's chrome | Each control's own keys, and nothing else |

`MqttPanelCardBackgroundBrush` is narrower than the name suggests. The Status block is a `Border` the
panel draws itself and takes the key; the toolkit's cards are the toolkit's own surface and take the
toolkit's. Pointing both at the same colour is what makes the band and the cards match.

Validation errors and the section rules resolve `SystemFillColorCriticalBrush`,
`DividerStrokeColorDefaultBrush` and `CardStrokeColorDefaultBrush`. Those are platform keys by
intent: a failed value reads as failure in the host's own language rather than in the panel's accent.

The screenshots show the six keys **alone**, under a palette nothing in the stock theme can produce
and with no toolkit or control override, so every surface in them is unambiguously one set or the
other: [light](docs/screenshots/mqtt-panel-light-branded.png) and
[dark](docs/screenshots/mqtt-panel-dark-branded.png).

`MqttPanelFontFamily` is the only route to the panel's typeface: every element the panel styles
carries an explicit style, and an explicit style means a host's implicit `TextBlock` style is never
applied. A toolkit `SettingsCard`'s own header keeps the stock face — the toolkit's style carries the
family — and everything else on the panel follows the key.

### Why the toolkit follows and the controls do not

**When a dictionary is parsed decides it.** The toolkit's dictionary loads on first use, after the
application's own entries exist, so its aliases resolve against them: overriding
`CardBackgroundFillColorDefaultBrush` and `TextFillColorPrimaryBrush` repaints the toolkit cards and
their headers. WinUI's dictionary is parsed at startup, before an application's entries exist, and
its aliases are frozen at that moment.

**So overriding the shared semantic brushes does not reach a WinUI control at all.**
`ComboBoxForeground` is a `StaticResource` alias to `TextFillColorPrimaryBrush`, resolved inside
WinUI's own dictionary as it is parsed. Replacing `TextFillColorPrimaryBrush` afterwards leaves the
alias pointing where it already pointed. Overriding the paired `*Color` key misses for the same
reason: the brush binds its colour with `StaticResource` as well. Accent-derived brushes are the one
partial exception, binding with `ThemeResource`, so replacing an accent colour does propagate —
which, beside the toolkit cards, is what makes the semantic brushes look like a general route.

**A control's own key is the one dependable lever.** `ComboBoxForeground`, `ComboBoxBackground`,
`TextControlForeground`, `ButtonBackground` and their per-state siblings are what a control template
looks up at run time, so declaring one as an immediate child of `Application.Resources` does reach
the control.

The module does not carry the control keys, because the surface is per control and large. Live brush
keys in WinUI's `Default` dictionary, for the controls this panel renders:

| Control | Brush keys |
|---|---|
| `CheckBox` | 72 |
| `ComboBox` | 39 |
| `ToggleSwitch` | 33 |
| `TextControl` — `TextBox` and `PasswordBox` | 32 |
| `Expander` | 31 |
| `ComboBoxItem` | 28 |
| `Button` | 12 |
| `SettingsCard` (toolkit) | 12 |
| `ContentDialog` | 7 |
| **Total** | **266** |

An application that wants branded dropdowns and fields declares the control keys it cares about in
its own resources, where they reach every page it has rather than this one panel. A panel key set
duplicating them would brand the panel and leave the rest of the application behind.

### Contrast

The module's defaults, rendered and sampled in both themes, over a flat page background and over a
Mica backdrop. Each ratio is the glyph against the ground it actually sits on, which for a card is
the composite of the card's translucent fill over the page.

| Tier | Light | Dark |
|---|---|---|
| Section heading, 15 px | 15.68 | 16.29 |
| Card header, 14 px (toolkit) | 16.65–17.08 | 9.44–14.16 |
| Status label, 14 px — body key | 16.65 | 14.16 |
| Descriptions and status values, 11 px — secondary key | 6.03–6.19 | 5.52–9.09 |
| Applied and unapplied markers, 11 px — accent key | 9.32–10.25 | 5.06–8.73 |
| Validation error, 11 px | 5.47 | 6.97 |
| Device-id dialogue, body 14 px | 17.22 | 14.16 |
| Device-id dialogue, secondary 11 px | 6.19 | 9.09 |

**The floor is 5.06:1** — the accent marker on a Broker card in the dark theme. Nothing the panel
renders sits below the 4.5:1 accessibility threshold.

A Mica backdrop moves none of it by more than 0.06. WinUI clamps the backdrop tint hard toward the
fallback base colour: a light page ground measures `#F1F3F9` against the flat theme's `#F3F3F3`. A
panel measured over a flat page background is therefore measured for Mica too, and that holds for
the surfaces with no ground of their own — the section headings and the translucent card fills.

**Measure after overriding**, in both themes: sample the panel's heading and the application's own
heading in the same frame — an override that arrived makes them identical, byte for byte — and
compute the contrast of the panel's text against its ground, where below 4.5:1 fails. A key that
fell back reads as the module's own default, which is exactly what the measurement separates from a
palette that is merely poor. `Capture 'MQTT panel' screenshots.ps1 -Branded` renders the panel under
an extreme palette for the same purpose.

## Translation

One `.resw` under `Strings\en-GB\`, read through the Windows App SDK's `ResourceManager` and the
`ResourceMap`s below it — several are tried, because where a library's strings land in the index
depends on how the consuming application builds. A consumer localises by adding a
language folder; a host with its own resource system supplies an `IMqttStringSource` on the setup
object instead. Everything falls back to the module's built-in en-GB, so a resource map that fails
to load leaves a readable panel rather than blank controls.

## What the panel never does

- It never writes a settings store directly — every commit goes through `IMqttSettingsStore.Update`,
  so a host whose configuration is one document keeps its own read-modify-write.
- **Test connection commits nothing at all**: not the fields, and not where the broker answered.
- **It never touches the network while a field is being edited.** The endpoint check runs on Test
  connection and on Apply, and on nothing else — not on a field settling, not on focus leaving a
  box, and not on the page being shown.
- Nothing in the Broker group takes effect until Apply, and an unapplied edit is marked beside the
  section heading rather than inside the group that holds the fields.
