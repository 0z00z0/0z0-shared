# The controls foundation assembly

`ZeroZero.Controls.WinUI` holds the WinUI controls that carry no studio identity — today the
settings-row info bubble. `net10.0-windows10.0.26100.0`, the Windows App SDK and nothing else: no
project reference, no font pack, no palette. That is what makes it **foundation** rather than a
component: any UI component may take it without dragging the brand assembly's font pack and About
window along, and the MQTT settings panel does, for the nineteen bubbles on its rows.

The assembly is versioned as `ControlsVersion` in `Versions.props` and released under
`controls-v<x.y.z>` tags, with notes under `docs/release-notes/controls/`;
[`releasing.md`](releasing.md) has the procedure. A component that references it can only release
once the version it references is on the feed, so a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later, with the Windows App SDK. Unpackaged. |

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

Not here, by design: anything carrying the studio's face or palette. Those are the brand component's
([`zerozero-brand.md`](zerozero-brand.md)), which a component takes when it needs them and says so.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Controls.WinUI` itself;
there is nothing beneath it. An application taking the MQTT module has it transitively and adds
nothing.

In markup, declare the namespace and place a bubble to the left of the field it explains, which is
the alignment rule the MQTT panel follows so that no adornment can displace a right edge:

```xml
<UserControl ... xmlns:shared="using:ZeroZero.Controls.WinUI">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <shared:InfoIcon Subject="the broker host" Info="Where the broker listens. A host name or an address; the port is separate."/>
        <TextBox Width="240"/>
    </StackPanel>
</UserControl>
```

In code, the same three properties: `new InfoIcon { Subject = row.InfoSubject, Info = row.Info }`.

## The harness

There is no unit test project for this assembly: a WinUI control needs the XAML runtime, and the
Windows App SDK bootstrapper hangs a runner with no desktop session rather than failing it (issue
#13). The control is looked at through `src/ZeroZero.Brand.WinUI.TestHarness` instead. `--mqtt`
renders the panel with every bubble on it; `--mqtt --info "MQTT Panel Light"` opens that one window
and the first bubble's flyout, so the flyout's own text can be seen and captured.
