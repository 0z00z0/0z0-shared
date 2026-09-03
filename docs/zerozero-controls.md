# The controls foundation assembly

`ZeroZero.Controls.WinUI` holds the WinUI controls that carry no studio identity: the settings-row
vocabulary — info bubble, section header, card row — title-bar theming, and the single-line text
prompt. `net10.0-windows10.0.26100.0`, the Windows App SDK, the Community Toolkit's settings
controls and `ZeroZero.Win32`; no font pack, no palette, and no toolkit type in a public signature.
That is what makes it **foundation** rather than a component: any UI component may take it without
dragging the brand assembly's font pack and About window along, and the MQTT settings panel does,
for the nineteen bubbles on its rows, as the settings shell does for its title bar.

The assembly is versioned as `ControlsVersion` in `Versions.props` and released under
`controls-v<x.y.z>` tags, with notes under `docs/release-notes/controls/`;
[`releasing.md`](releasing.md) has the procedure. It references `ZeroZero.Win32`, so it releases
after `win32`; a component that references it can only release once the version it references is
on the feed, so a change here releases before that component.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later, with the Windows App SDK. Unpackaged. |
| Toolkit | `CommunityToolkit.WinUI.Controls.SettingsControls` at the family's pin, which is a ceiling for a consumer's own direct reference ([`consuming.md`](consuming.md#third-party-pins)). |

## What it contains

- **`InfoIcon`** — a small "(i)" button that opens its explanation in a flyout: the one place a
  settings row puts the how-it-works detail that would otherwise sit in the visible copy. A flyout
  rather than a tooltip, because an explanation of several sentences cannot live somewhere that
  closes as the pointer moves; a button rather than a bare glyph, because a button's flyout already
  opens on Space and Enter. `Info` is the text, `Subject` is what it is about ("the broker host"),
  and the accessible name is composed from it — "More information about the broker host" — so a
  screen reader meeting several bubbles on one page hears which setting each one explains.
  `GlyphCode` overrides the glyph for a host whose icon set names it differently, and an empty value
  falls back to the stock "info" glyph. All three are dependency properties, so a row built in code
  binds them. The glyph names Segoe Fluent Icons with Segoe MDL2 Assets as its fallback, so it still
  draws on a Windows older than 11, and the foreground resolves a stock theme brush, so the bubble
  follows the host's theme with no key to declare.
- **`SettingsSectionHeader`** — the sub-heading that opens a group of rows: a rule, the heading,
  an optional bubble and a `Trailing` slot for a marker such as "Unapplied changes". The rule sits
  above the heading and carries the gap between groups, so a heading binds downwards to its own
  cards; `ShowDivider` is off for the first group on a page. `Heading`, `Info` and `InfoSubject`
  are the bubble's inputs, the bubble shows only when `Info` holds text, and the subject defaults
  to the heading. Size, weight and spacing are the heading's own — the MQTT panel's sub-header,
  lifted — while colour and face are inherited: a host restyles by setting `Foreground` or
  `FontFamily` on the instance, and nothing in the control outranks that.
- **`SettingsRow`** — one settings row on the toolkit's card: `Header` and `Description` (a string
  or an element each), `Info` and `InfoSubject` for the bubble, and `Field`, the control that edits
  the setting. The bubble sits to the left of the field, so a row with one and a row without share
  the same right edge — the panel's alignment contract. `FieldWidth` holds the field to one width,
  the panel's one-width-per-page rule; left unset the field keeps its natural size, which is what
  a toggle wants. The card wraps on its own when it is narrower than the toolkit's threshold
  (about 476 device-independent units): the field drops beneath the header, left-aligned. That is
  the toolkit's behaviour, driven by the card's width and not the window's, and the harness shows
  it at 420.
- **`TitleBarTheming`** — `Apply(window, theme)` paints the system title bar for a theme, and
  `Follow(window)` paints it for the content's theme now and on every live change. Mica does not
  paint the caption area, so a dark page whose bar is left alone shows a light strip behind its
  caption buttons; dark gets `TitleBarPalette.Dark`, or a palette the caller passes. An application
  pinned dark passes `ElementTheme.Dark`; one that follows the system calls `Follow` once. Two
  facts, measured in the harness, shape the light case: a bar that has been painted once cannot be
  handed back to the system — its colours set to null draw black — and an untouched bar is the
  light one. So on light an untouched bar is left alone, and a bar that was dark is painted
  `TitleBarPalette.Light`, which is within two units of untouched. `TitleBarPalette` is the twelve
  caption colours as plain ARGB values, framework-free so a test can pin both sets. Does nothing on
  a Windows whose title bar cannot be recoloured.
- **`TextPromptWindow`** — `ShowAsync(TextPromptOptions)`: a frameless Mica window centred on the
  monitor under the cursor, with a title, a message, one field, an optional note beneath it, and
  an equal-width cancel-and-confirm row with the confirm on the right. Resolves with the text on
  confirm and null on cancel, Escape or any other way out; Enter confirms from the field; the
  confirm button waits for text unless `AllowEmpty` is set. Every string is the caller's —
  `Confirm` says what the answer does, "Rename", not "OK" — and so is the theme, so an application
  pinned dark passes it. The prompt collapses the field's selection before it closes: closing with
  the opening selection still in place crashed the process inside the XAML runtime, measured.

Not here, by design: anything carrying the studio's face or palette. Those are the brand component's
([`zerozero-brand.md`](zerozero-brand.md)), which a component takes when it needs them and says so.
No theme keys either: every control resolves stock brushes, and a host recolours a heading through
the properties it inherits.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Controls.WinUI` itself,
which brings `ZeroZero.Win32` and the toolkit with it. An application taking the MQTT module or the
settings shell has it transitively and adds nothing.

A settings page in markup — the header, then rows with a field held to one width and a toggle at
its natural size:

```xml
<UserControl ... xmlns:shared="using:ZeroZero.Controls.WinUI">
    <StackPanel Spacing="4">
        <shared:SettingsSectionHeader Heading="Connection" ShowDivider="False"
                                      Info="Where the broker is reached. Nothing here is saved until Apply."/>
        <shared:SettingsRow Header="Host" FieldWidth="240"
                            Info="A host name or an address; the port is separate.">
            <shared:SettingsRow.Field>
                <TextBox TextAlignment="Right"/>
            </shared:SettingsRow.Field>
        </shared:SettingsRow>
        <shared:SettingsRow Header="Publish" Description="Off by default.">
            <shared:SettingsRow.Field>
                <ToggleSwitch/>
            </shared:SettingsRow.Field>
        </shared:SettingsRow>
    </StackPanel>
</UserControl>
```

In code, the same properties: `new SettingsRow { Header = row.Label, Info = row.Info, Field = toggle }`.
A bubble on its own, to the left of the field it explains: `<shared:InfoIcon Subject="the broker host" Info="..."/>`.

The title bar, in a window's constructor after its content is set — `TitleBarTheming.Follow(this)`
for a window that follows the system, `TitleBarTheming.Apply(this, ElementTheme.Dark)` for an
application pinned dark.

The prompt, from a tray menu or a settings page:

```csharp
string? name = await TextPromptWindow.ShowAsync(new TextPromptOptions
{
    Title = "Rename device",
    Message = "The name the device is announced under.",
    Confirm = "Rename",
    Note = "Applies on the next publish.",
    InitialText = current,
});
if (name is not null) Rename(name);
```

## Tests and the harness

`tests/ZeroZero.Controls.Tests` is a plain `net10.0` project that references no WinUI assembly:
a WinUI control needs the XAML runtime, and the Windows App SDK bootstrapper hangs a runner with
no desktop session rather than failing it (issue #13). What can be tested without the runtime is:
the title-bar enum and palette, compiled in as linked source and pinned — opaque throughout,
grounds the window colour, glyph and fill ordering, no value shared between the two sets — and
the controls' markup read as data beside the panel's, so the section header keeps the panel's
sub-header typography and rule, the row keeps its bubble before its field, and the prompt keeps
its confirm on the right of two equal columns.

Everything rendered is looked at through `src/ZeroZero.Brand.WinUI.TestHarness`. `--rows` opens
the row vocabulary in both themes at a page's width and at one narrow enough to wrap; `--titlebar`
opens five Mica windows that put an untreated dark bar beside the painted, followed, pinned and
reverted ones; `--prompt` opens the prompt in both themes, and `--prompt --confirm "<text>"` or
`--prompt --cancel` answers one through its own field and button and writes what its task resolved
with to `text-prompt-result.txt` in the temp folder. `--mqtt --info "MQTT Panel Light"` opens the
panel's first bubble, so the flyout's own text can be seen and captured.
