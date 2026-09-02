# The brand component

The studio's visual identity and About plumbing: the brand constants, one parameterised About
component with a popup window to host it, the settings-row info icon and the brand typeface.
`ZeroZero.Brand.Core` holds the constants and the data contracts; `ZeroZero.Brand.WinUI` holds the
controls and the window.

The component is versioned as `BrandVersion` in `Versions.props` and released under `brand-v<x.y.z>`
tags, with notes under `docs/release-notes/brand/`; [`releasing.md`](releasing.md) has the procedure.
The entry point is `ZeroZero.Brand.WinUI`, which brings `ZeroZero.Brand.Core` with it; a console tool
takes `ZeroZero.Brand.Core` alone.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| `ZeroZero.Brand.Core` | Plain `net10.0` — no WinUI, no Windows-specific dependencies, safe from a console app or any other .NET target. |
| `ZeroZero.Brand.WinUI` | `net10.0-windows10.0.26100.0`, Windows 10 1809 (build 10.0.17763) or later, with the Windows App SDK. Unpackaged. |

## The assemblies

### `ZeroZero.Brand.Core`

- **`Brand`** — studio-wide constants: name, tagline, website, Buy Me a Coffee URL, GitHub org URL,
  and the brand palette as hex strings (teal / blue / purple / indigo / amber, plus the two background
  tones).
- **`ExternalLibrary`** — a small record describing a third-party dependency to credit (name, author,
  purpose, licence, optional URL).
- **`AboutInfo`** — the per-app data an About surface needs: app name, version, description, repo
  URL, and its list of `ExternalLibrary` credits.
- **`ConsoleBanner`** — prints a plain-ASCII "about" banner to the console for non-UI (CLI) tools,
  built from an `AboutInfo`.

### `ZeroZero.Brand.WinUI`

References `ZeroZero.Brand.Core`.

- **`BrandAboutControl`** — a `UserControl` holding the actual About *content*: the `[Ø]` studio mark
  and brand header band, the company name and tagline as plain non-interactive text, app description,
  three co-equal link buttons (repository / website / donate), an expandable external-libraries credit
  list, and a copyright footer. Owns no window chrome, sizing, or update/exit flow — hosts either
  inside `BrandAboutWindow` or directly inside a host app's own in-navigation page. Call
  `SetInfo(AboutInfo)` after construction to populate it (a method, not a settable property — the
  WinUI XAML compiler needs a parameterless constructor for any type exposed as a public property on
  a XAML class, which `AboutInfo`'s `required` members deliberately do not have).
- **`BrandAboutWindow`** — the shared, parameterised About popup (320 px wide, Mica backdrop, centred
  on the monitor under the cursor, no title bar, always-on-top). A thin shell hosting
  `BrandAboutControl` plus the tray-app-only "Check for Updates" button. Takes its monitor and DPI
  metrics from the `ZeroZero.Win32` foundation assembly, so it has no dependency on a consuming
  app's own `NativeMethods` class.
- **`BrandAboutOptions`** — the parameters: an `AboutInfo`, an optional `OnCheckForUpdates` callback
  (omit it to hide the "Check for Updates" button entirely — a console-only tool or a build without
  an update channel does not pass one), and an optional `OnBeforeExit` hook for apps that need to
  self-exit cleanly before an installer-triggered relaunch.
- **`InfoIcon`** — a small "(i)" button that opens its explanation in a flyout, for the settings row
  whose how-it-works detail would otherwise sit in the visible copy. `Info`, `Subject` and
  `GlyphCode` are dependency properties, so a row built in code can bind them. It carries no brand
  vocabulary and resolves stock theme brushes; the MQTT settings panel takes it from here.
- **The brand typeface**, Cascadia Mono, with its OFL licence. Shipped as content so it travels with
  the library into every consuming app's output under `Assets\Fonts\`, where `BrandAboutWindow`
  references it by relative path; inside the package it sits beside the assembly under
  `lib\<tfm>\ZeroZero.Brand.WinUI\Assets\Fonts\`, the folder a consuming WinUI build resolves a
  referenced library's assets from.

Deliberately **not** shared: each app's own update-check networking and dialogue plumbing. Only the
window chrome and layout are unified — `OnCheckForUpdates` is a plain `Func<Task<bool>>` the consumer
wires up to its own existing update flow, returning `true` when an update was applied so the window
owns the clean-exit-before-relaunch step via `OnBeforeExit`.

## Take the reference

Either route in [`consuming.md`](consuming.md) — a `PackageReference` on the studio feed, or a
`ProjectReference` on a sibling checkout carrying
`<UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>`. The reference is
`ZeroZero.Brand.WinUI`; it pulls in `ZeroZero.Brand.Core` and `ZeroZero.Win32` transitively and
ships the typeface as content, so a consumer gets the correct brand face with no extra setup. The consuming app's
`app.manifest` declares `PerMonitorV2` DPI awareness so the window renders sharp on high-DPI
displays.

## Pick the hosting style

Both share the same `AboutInfo` data model — the choice is whether the consuming app has a separate
About *window* or an About *page*:

| | Tray app | Full windowed app |
|---|---|---|
| Component | `BrandAboutWindow` | `BrandAboutControl` |
| Surface | Standalone popup (Mica, no title bar, always-on-top) | Hosted inside the app's own `Page` or window |
| "Check for Updates" | Yes, via `BrandAboutOptions` | No — not this layer's concern |

### The tray-app popup

Open the window with data only — no per-app XAML or logic duplication:

```csharp
var options = new BrandAboutOptions
{
    Info = new AboutInfo
    {
        AppName           = "ExampleApp",
        Version           = "1.2.3",
        Description       = "What the app does.",
        RepoUrl           = "https://github.com/0z00z0/ExampleApp",
        ExternalLibraries = [ new ExternalLibrary("SomeLib", "Some Author", "What it's for", "MIT", "https://...") ],
    },
    OnCheckForUpdates = async () => await ExampleApp.Services.UpdateCheckService.CheckNowAsync(...),
    OnBeforeExit      = async () => { await ExampleApp.ShutdownAsync(); return true; },
};

new BrandAboutWindow(options).Activate();
```

**The update-check contract** — both callbacks are optional:

- **`OnCheckForUpdates`** (`Func<Task<bool>>`) — wired to the consuming app's own update flow.
  Return `true` when an update was applied and the window drives the clean exit (so the installer
  can relaunch); return `false` when there was nothing to update and the window stays open.
  **Omit it entirely to hide the "Check for Updates" button** — a console-only tool, or a build with
  no update channel.
- **`OnBeforeExit`** (`Func<Task<bool>>`) — run just before an update-triggered close so the app can
  tear down cleanly; return `false` to veto the exit and keep the window open.

The window owns only chrome and layout; each app keeps its own update-check networking and dialogue
plumbing and wires it in through these two callbacks.

### Hosted in the application's own page

A full windowed app whose About is an in-navigation `Page` (not a separate popup, and with no
"check for updates" concept) skips `BrandAboutWindow` entirely and hosts the content control itself.
[`consume-brand-about-control.md`](consume-brand-about-control.md) is the same as a checklist.

**1. Add the control to the app's existing About page XAML**, in place of the bespoke layout:

```xml
<!-- The consuming app's own AboutPage.xaml -->
<Page ... xmlns:brand="using:ZeroZero.Brand.WinUI">
    <ScrollViewer>
        <brand:BrandAboutControl x:Name="About" MaxWidth="560" HorizontalAlignment="Center"/>
    </ScrollViewer>
</Page>
```

**2. Populate it from the app's existing brand-facts source** (whatever plays the same role as this
repository's `AboutInfo` — a `BrandInfo` static class that also feeds a CLI banner, say):

```csharp
// AboutPage.xaml.cs
public AboutPage()
{
    InitializeComponent();
    About.SetInfo(new AboutInfo
    {
        AppName           = AppBrandInfo.Product,
        Version           = AppBrandInfo.Version,
        Description       = AppBrandInfo.Description,
        RepoUrl           = AppBrandInfo.RepositoryUrl,
        ExternalLibraries = AppBrandInfo.ExternalLibraries
            .Select(l => new ExternalLibrary(l.Name, l.Author, l.Purpose, l.License))
            .ToList(),
    });
}
```

`SetInfo` is a method rather than a settable property — **call it exactly once, from the hosting
page's constructor or its `Loaded` handler**, after `InitializeComponent`.

**3. Delete the bespoke About view-model and layout** once the control renders correctly; keeping
both is what lets them drift. The app's own brand-facts class stays as the single source of truth —
only its *rendering* moves to the shared control, not its data.

**Notes:**

- The control inherits the host page's theme (everything but the fixed-colour brand header band
  uses `ThemeResource` brushes), so no extra theming work is needed.
- Never shows an update button — there is no `BrandAboutOptions` and no update-flow concept at this
  layer. An app that does need an update check on its About surface is a case for
  `BrandAboutWindow` instead.
- The control supplies the `[Ø]` studio mark, the company name and the tagline itself, from `Brand`'s
  studio-wide constants. Of the three link buttons — **Repository / Website / Donate** — only
  `RepoUrl` comes from the `AboutInfo`; Website and Donate always point at the studio's own
  `Brand.WebsiteUrl` / `Brand.BuyMeACoffeeUrl` rather than anything per-app. None of those five are
  supplied by the consumer.

## Screenshots

**`BrandAboutWindow`** (tray-app popup):

![BrandAboutWindow](screenshots/about-window.png)

**`BrandAboutControl`** hosted directly in a plain window (no popup chrome, no update button):

![BrandAboutControl hosted](screenshots/about-hosted-control.png)

Both images are the capture script's output, so they show the surfaces as they actually render
rather than what the XAML claims.

## The harness

`src/ZeroZero.Brand.WinUI.TestHarness` is a minimal WinUI exe that opens both hosting scenarios with
this repository's own sample data, so the About content can be inspected on screen without building
or running a consuming application:

```powershell
dotnet run --project src/ZeroZero.Brand.WinUI.TestHarness
```

It opens two windows: the `BrandAboutWindow` popup ("Window Mode") and a plain window hosting
`BrandAboutControl` directly with ordinary title-bar chrome and no update button ("Hosted Control
Demo"). With `--mqtt` it opens the MQTT panel scenario instead — one component per run, so unrelated
windows never land on top of each other.

Two scripts under `scripts/` drive the About scenarios:

- **`Show live 'About' dialogue.ps1`** — builds the harness if its exe is missing, then launches it,
  so both windows can be inspected on screen.
- **`Capture 'About' screenshot.ps1`** — launches the harness and writes window-only PNGs of both
  scenarios into `docs/screenshots/`: `about-window.png` (the popup) and `about-hosted-control.png`
  (the hosted control), the two images this guide embeds. Capture goes through `PrintWindow` with
  `PW_RENDERFULLCONTENT`, so the translucent Mica backdrop resolves cleanly and no desktop content
  bleeds through; the two windows are told apart by their `AppWindow` title, not creation order.
