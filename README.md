# ZeroZero Software — shared branding library

Shared visual identity and About-window plumbing for ZeroZero Software's desktop apps
(currently [ChargeKeeper](https://github.com/0z00z0/ChargeKeeper) and
[HyperVManagerTray](https://github.com/0z00z0/HyperVManagerTray)). Both apps shipped
near-identical, hand-copied `AboutWindow.xaml` popups that had started to drift; this repo
unifies them into one parameterized component and centralizes the studio's brand constants
(name, tagline, palette, links) so future apps don't have to re-type them.

MIT licensed, public.

## Projects

### `src/ZeroZero.Brand.Core`

Plain `net10.0` — no WinUI, no Windows-specific dependencies, safe to reference from a console
app or any other .NET target. Contains:

- **`Brand.cs`** — studio-wide constants: name, tagline, website, Buy Me a Coffee URL, GitHub org
  URL, and the brand palette as hex strings (teal / blue / purple / indigo / amber, plus the two
  background tones).
- **`ExternalLibrary.cs`** — a small record describing a third-party dependency to credit
  (name, author, purpose, license, optional URL).
- **`AboutInfo.cs`** — the per-app data an About surface needs: app name, version, description,
  repo URL, and its list of `ExternalLibrary` credits.
- **`ConsoleBanner.cs`** — prints a plain-ASCII "about" banner to the console for non-UI (CLI)
  tools, built from an `AboutInfo`.

### `src/ZeroZero.Brand.WinUI`

`net10.0-windows10.0.26100.0`, WinUI 3 / Windows App SDK, unpackaged. References
`ZeroZero.Brand.Core`. Contains:

- **`BrandAboutControl`** — a `UserControl` holding the actual About *content*: the `[Ø]` studio
  mark + brand header band, company name/tagline (linked to the studio site), app description,
  three co-equal link buttons (repository / website / donate), an expandable external-libraries
  credit list, and a copyright footer. Owns no window chrome, sizing, or update/exit flow — hosts
  either inside `BrandAboutWindow` (tray-app popup) or directly inside a host app's own
  in-navigation page (a full windowed app with no separate About window and no update concept).
  Call `SetInfo(AboutInfo)` after construction to populate it (a method, not a settable property —
  the WinUI XAML compiler needs a parameterless constructor for any type exposed as a public
  property on a XAML class, which `AboutInfo`'s `required` members deliberately don't have).
- **`BrandAboutWindow`** — the shared, parameterized About popup (320px wide, Mica backdrop,
  centered on the monitor under the cursor, no title bar, always-on-top), now a thin shell hosting
  `BrandAboutControl` plus the tray-app-only "Check for Updates" button. Replaces each app's own
  hand-rolled `AboutWindow`. Carries its own minimal Win32 P/Invoke for monitor/DPI metrics, so it
  has no dependency on a consuming app's own `NativeMethods` class.
- **`BrandAboutOptions`** — the parameters: an `AboutInfo`, an optional `OnCheckForUpdates`
  callback (omit it to hide the "Check for Updates" button entirely — a console-only tool or a
  build without an update channel just doesn't pass one), and an optional `OnBeforeExit` hook for
  apps that need to self-exit cleanly before an installer-triggered relaunch.

Deliberately **not** shared: each app's own update-check networking/dialog plumbing
(`UpdateCheckService`, `UpdateChecker`, `UpdatePrompt`, etc.). Only the window chrome and layout
are unified — `OnCheckForUpdates` is a plain `Func<Task<bool>>` the consumer wires up to its own
existing update flow (returning `true` when an update was applied so the window owns the
clean-exit-before-relaunch step via `OnBeforeExit`).

### `src/ZeroZero.Brand.WinUI.TestHarness`

A minimal WinUI exe that opens both hosting scenarios with this repo's own sample data — run it to
eyeball the About content on screen without building or running ChargeKeeper, HyperVManagerTray, or
M365Migrator:

```powershell
dotnet run --project src/ZeroZero.Brand.WinUI.TestHarness
```

It opens two windows: the `BrandAboutWindow` popup ("Window Mode") and a plain window hosting
`BrandAboutControl` directly with ordinary title-bar chrome and no update button ("Hosted Control
Demo") — simulating a full windowed app's in-navigation About page.

## Screenshots

**`BrandAboutWindow`** (tray-app popup):

![BrandAboutWindow](docs/screenshots/about-window.png)

**`BrandAboutControl`** hosted directly in a plain window (no popup chrome, no update button):

![BrandAboutControl hosted](docs/screenshots/about-hosted-control.png)

*Updated from the test harness whenever either surface is visually verified — always reflects the
current on-screen appearance, not just what the XAML claims.*

## Integrating the About dialogue

**1. Reference the library.** Add a `ProjectReference` to a sibling checkout of this repo (there is
no NuGet feed yet — tracked in [issue #1](https://github.com/0z00z0/0z0-shared/issues/1)):

```xml
<ProjectReference Include="..\0z0-shared\src\ZeroZero.Brand.WinUI\ZeroZero.Brand.WinUI.csproj" />
```

`ZeroZero.Brand.WinUI` pulls in `ZeroZero.Brand.Core` transitively and ships the brand typeface
(Cascadia Mono) as content, so a consumer gets the correct font with no extra setup. Your app's
`app.manifest` should declare `PerMonitorV2` DPI awareness (as ChargeKeeper and HyperVManagerTray
already do) so the window renders sharp on high-DPI displays.

> In each consumer's own GitHub Actions CI, add an `actions/checkout` step for `0z00z0/0z0-shared`
> into the sibling path before building — the runner only checks out one repo, so the relative
> `ProjectReference` won't resolve otherwise.

**2. Open the window with data only** — no per-app XAML or logic duplication:

```csharp
var options = new BrandAboutOptions
{
    Info = new AboutInfo
    {
        AppName           = "YourApp",
        Version           = "1.2.3",
        Description       = "What your app does.",
        RepoUrl           = "https://github.com/0z00z0/YourApp",
        ExternalLibraries = [ new ExternalLibrary("SomeLib", "Some Author", "What it's for", "MIT", "https://...") ],
    },
    OnCheckForUpdates = async () => await YourApp.Services.UpdateCheckService.CheckNowAsync(...),
    OnBeforeExit      = async () => { await YourApp.ShutdownAsync(); return true; },
};

new BrandAboutWindow(options).Activate();
```

**The update-check contract** — both callbacks are optional:

- **`OnCheckForUpdates`** (`Func<Task<bool>>`) — wired to your app's own update flow. Return `true`
  when an update was applied and the window drives the clean exit (so the installer can relaunch);
  return `false` when there was nothing to update and the window stays open. **Omit it entirely to
  hide the "Check for Updates" button** — e.g. a console-only tool or a build with no update channel.
- **`OnBeforeExit`** (`Func<Task<bool>>`) — run just before an update-triggered close so your app
  can tear down cleanly; return `false` to veto the exit and keep the window open.

The window owns only chrome and layout; each app keeps its own update-check networking/dialog
plumbing and wires it in through these two callbacks.

**No popup window? Host `BrandAboutControl` directly.** A full windowed app whose About is an
in-navigation `Page` (not a separate popup, and with no "check for updates" concept) skips
`BrandAboutWindow` entirely and hosts the content control itself:

```xml
<!-- YourApp's own AboutPage.xaml -->
<Page ... xmlns:brand="using:ZeroZero.Brand.WinUI">
    <ScrollViewer>
        <brand:BrandAboutControl x:Name="About" MaxWidth="560" HorizontalAlignment="Center"/>
    </ScrollViewer>
</Page>
```

```csharp
// AboutPage.xaml.cs
About.SetInfo(new AboutInfo { AppName = "YourApp", Version = "1.2.3", /* … */ });
```

The control inherits your page's theme (everything but the fixed-color brand header band uses
`ThemeResource` brushes) and never shows an update button — there's no `BrandAboutOptions` and no
update-flow concept at this layer.

## Package versions

`Microsoft.WindowsAppSDK` and `Microsoft.Windows.SDK.BuildTools` versions are pinned to match
what ChargeKeeper and HyperVManagerTray already use (`2.1.3` / `10.0.28000.1839` at the time of
writing) so all three projects resolve the same Windows App SDK runtime.

## Build

```powershell
dotnet build 0z0-shared.slnx
```

## License

[MIT](LICENSE) © ZeroZero Software ([0z0.xyz](https://0z0.xyz))
