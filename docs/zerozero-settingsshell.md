# The settings window shell

`ZeroZero.SettingsShell.WinUI` is the settings window both applications converged on, with every
page left to the application: Mica chrome with the system title bar painted for the theme, a left
navigation pane with a product footer, one scroll viewer over the pages, placement against the
application's saved rectangle, Escape to close, and a section lifecycle — an enter and a leave
hook around every change of section, and a per-section build-once flag a rebuild honours.
`net10.0-windows10.0.26100.0`, the Windows App SDK and `ZeroZero.Controls.WinUI`; no font pack,
no palette, no theme keys and no strings of its own.

The assembly is versioned as `SettingsShellVersion` in `Versions.props` and released under
`settingsshell-v<x.y.z>` tags, with notes under `docs/release-notes/settingsshell/`;
[`releasing.md`](releasing.md) has the procedure. It references `ZeroZero.Controls.WinUI`, so it
releases after `controls`, which releases after `win32`.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later, with the Windows App SDK. Unpackaged. |
| Toolkit | `CommunityToolkit.WinUI.Controls.SettingsControls` arrives through `ZeroZero.Controls.WinUI`, at the family's pin, which is a ceiling for a consumer's own direct reference ([`consuming.md`](consuming.md#third-party-pins)). |

## The division

The shell renders the window; the application supplies the pages. Inside the shell: the window
and its Mica backdrop, the title bar painted for the theme, the navigation pane with no toggle,
no settings item and no back button, the transparency overrides that let the backdrop through
pane and content alike, the icon box at 28 units, the pane footer fed a mark, a name and a
version, one scroll viewer over the page host, the tag-to-page map with its selection dispatch,
placement against the rectangle store with the clamp to a monitor and the centring on the
cursor's monitor, the refusal to remember a maximised or minimised rectangle, and Escape.

Supplied by the application: an ordered list of sections — tag, label, icon, a build function,
the two optional hooks and the build-once flag — the rectangle store, the product triple for the
footer, the theme, and the few measurements the two applications choose differently: the default
client size, the pane width, the page width cap and the page padding.

## What it contains

- **`SettingsWindow`** — the window. `Navigate(tag)` shows a section; `CurrentTag` says which is
  on screen; `Rebuild()` discards and builds again every page whose section is not build-once,
  `Rebuild(tag)` one page; `FitToPages()` sizes the window so the tallest page fits. The
  application manages it as a singleton: opens one, hands `Navigate` to whatever wants a section,
  and lets go of it on `Closed`.
- **`SettingsWindowSetup`** — everything the window takes: `Title`, `Sections`, `InitialTag`
  (the first declared when null), `Theme` (Default follows the application; an application pinned
  to one theme passes it), `RectStore` (none opens centred on the cursor's monitor every time),
  `DefaultClientWidth` and `DefaultClientHeight` (960 × 640), `PaneWidth` (224), `PageMaxWidth`
  (unbounded), `PagePadding` (24, 20, 24, 24), `ProductMark`, `ProductName` and
  `ProductVersion`. All in device-independent units.
- **`SettingsSection`** — one pane entry and its page: `Tag`, `Label`, `Icon` (an `IconSource`:
  a font glyph, a bitmap or an SVG through `ImageIconSource`), `Build`, `Enter`, `Leave` and
  `BuildOnce`.
- **`IWindowRectStore`** and **`WindowRect`** — where the outer rectangle is kept between runs,
  as four integers in physical pixels behind `Load` and `Save`. The application's own settings
  document, which the shell never sees. Anything either member throws comes back to the
  application.

## The lifecycle

Every page is built as the window opens, in declaration order, and stays in the window hidden
while another section is current. A page that leaves the screen keeps everything — a staged
edit, an open group, a scroll position of its own — and comes back as it was; nothing but a
rebuild discards one. The hooks run in a fixed order around a change: the old section's `Leave`
while its page is still on screen, then the change, then the new section's `Enter` once its page
is. Selecting the current section again does nothing. On `Closed` the current section leaves;
no page is discarded.

`Rebuild()` discards and builds again every page whose section is not build-once. The current
section, if rebuilt, leaves before its page goes and enters again on the new one; the others are
rebuilt hidden. `Rebuild(tag)` does the same for one section, and is refused with an
`InvalidOperationException` for a build-once section: the flag exists to keep a page whose state
a rebuild would lose, so a request naming it is a mistake rather than an exception to the rule.

`FitToPages()` measures every page at the width the pages have now — each made visible for its
own measure and put back — and grows the window to the tallest, widening only for a page that
cannot fit the width it has, within the work area of the monitor it is on. Everything conditional
about a page is therefore decided inside `Build`, which has run for every section before any
measure. Called before the content has loaded, it waits for load, so an application calls it
straight after the constructor.

Two shapes of application, and the shell serves both without either bending:

- **Every page built up front, measured together, the window fitted.** `Build` for each section
  decides its conditional content; `FitToPages()` after construction; `Enter` and `Leave` on
  the one page with a timer; `Theme = ElementTheme.Dark` for an application pinned dark.
- **Everything rebuilt on a change, one page never.** `Rebuild()` on the change, with
  `BuildOnce` on the section holding a panel that is initialised once and stages edits; a page
  whose content arrives later returns its container from `Build` and fills it when the content
  comes; `Rebuild(tag)` for the one page a narrower change touches.

What the shell does not offer, stated so an application does not look for it: pages are built
eagerly, never on first visit; there is one scroll viewer for every page, reset to the top on
each change of section; the hooks are synchronous; a rebuild is immediate, not deferred to the
next visit; and `FitToPages()` measures at the current width, so a page whose text wraps measures
for the width it has, not the width it would like.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.SettingsShell.WinUI`
itself, which brings `ZeroZero.Controls.WinUI`, `ZeroZero.Win32` and the toolkit with it. An
application taking the MQTT module has the first two already.

Sections, the window, and the panel from [`consume-mqtt-settings-panel.md`](consume-mqtt-settings-panel.md)
as one of them:

```csharp
MqttSettingsPanel? panel = null;

var window = new SettingsWindow(new SettingsWindowSetup
{
    Title = "Settings",
    Sections =
    [
        new SettingsSection
        {
            Tag = "general", Label = "General",
            Icon = new FontIconSource { Glyph = "" },
            Build = () => new GeneralPage(settings),
        },
        new SettingsSection
        {
            Tag = "mqtt", Label = "Home Assistant",
            Icon = new ImageIconSource { ImageSource = new SvgImageSource(new Uri("ms-appx:///Assets/mqtt.svg")) },
            BuildOnce = true,
            Build = () =>
            {
                panel = new MqttSettingsPanel();
                panel.Initialise(mqttSetup);
                return new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "MQTT", FontSize = 20 }, panel } };
            },
            Enter = () => panel?.Refresh(),
        },
    ],
    Theme = ElementTheme.Dark,
    RectStore = new SettingsWindowRectStore(settings),
    ProductMark = new SvgImageSource(new Uri("ms-appx:///Assets/mark.svg")),
    ProductName = "The product",
    ProductVersion = version,
    PageMaxWidth = 720,
});
window.Closed += (_, _) => { panel?.Cancel(); _settings = null; };
window.Activate();
```

A teardown that must run whichever section is current — the panel's `Cancel`, which abandons a
probe in flight — goes on `Closed`, not on the section's `Leave`: `Leave` runs on close only for
the section on screen, and the panel's `Cancel` is final, so a `Leave` that called it would leave
a dead page for the next visit.

The rectangle store, over the application's own document:

```csharp
sealed class SettingsWindowRectStore(SettingsFile file) : IWindowRectStore
{
    public WindowRect? Load() => file.Read().SettingsWindow is { } r ? new WindowRect(r.X, r.Y, r.Width, r.Height) : null;
    public void Save(WindowRect rect) => file.Update(s => s with { SettingsWindow = new(rect.X, rect.Y, rect.Width, rect.Height) });
}
```

## Placement

The store is asked once as the window opens and told once as it closes. A saved rectangle is
kept where it was, shrunk to the work area of the monitor nearest it if it has outgrown that,
and moved inside it if it has strayed — a monitor that has gone leaves a rectangle nothing can
reach. With nothing saved, the default client size at the cursor monitor's scale, centred on that
monitor's work area, which is the screen whose tray was just clicked. The rectangle told back is
the last one seen while the window was neither maximised nor minimised, so closing from either
state saves the geometry the user last chose rather than the presenter's.

## Theming

`Theme` left at Default follows the application; an application pinned to one theme passes it,
and the title bar follows the same value through `TitleBarTheming` in the controls assembly, so
a dark page never sits under a light caption strip. The shell declares no theme keys: the pane
and the content are transparent over the backdrop, the footer's version line takes the stock
secondary text brush, and every page brings its own look. A host that brands its pages brands
them as pages, and the shell shows what it is given.

## Traps

- **A build-once section named by `Rebuild(tag)` throws.** Rebuild everything with `Rebuild()`
  and let the flag exempt the section, or name a section that is not build-once.
- **`Build` and the hooks are the application's code and their exceptions propagate** — from
  the constructor for `Build`, from `Navigate` and `Rebuild` for the hooks. A page that may fail
  to build catches inside `Build` and returns what it can.
- **Escape closes the window from any control that does not take Escape for itself.** A
  drop-down or a flyout takes it first and closes itself; a text box does not, so Escape in a
  field closes the window, the way it does in the two applications.
- **Two windows from one section list.** `IconSource` and `Build` are reusable across windows;
  an element built for one window is not, so a section list that is kept and reused must not
  hold elements — only functions that make them.

## Tests and the harness

`tests/ZeroZero.SettingsShell.Tests` is a plain `net10.0` project that references no WinUI
assembly: a WinUI window needs the XAML runtime, and the Windows App SDK bootstrapper hangs a
runner with no desktop session rather than failing it (issue #13). What can be tested without the
runtime is compiled in as linked source: the section lifecycle, which pins the order of every hook
and visibility change, the build-once rule and the refusals; and the placement arithmetic — the
centring, the clamp, the shrink, the empty rectangle and the maximised one refused. The window's
markup is read as data: Mica, the pane's five settings, the six overrides, the icon box, the one
star column and the footer's order.

Everything rendered is looked at through `src/ZeroZero.Brand.WinUI.TestHarness`. `--settings`
opens the shell in both themes with four sections — a page from the row vocabulary, the MQTT
panel built once, a page whose timer runs only while it is on screen, and the About control
hosted in navigation — and logs every build, hook, load and save to `settings-shell-log.txt` in
the temp folder. `--only Light` or `--only Dark` opens one; `--fit` fits it; `--rect X,Y,W,H`
seeds the rectangle store; `--navigate a,b,c` walks the sections; `--rebuild`, `--maximise` and
`--close-after <ms>` take the steps a saved-rectangle measurement needs.
