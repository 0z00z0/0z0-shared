# ZeroZero Software — shared branding library

Shared visual identity and About-window plumbing for ZeroZero Software's desktop apps
(currently [ChargeKeeper](https://github.com/0z00z0/ChargeKeeper) and
[HyperVManagerTray](https://github.com/0z00z0/HyperVManagerTray)). One parameterized About
component, plus the studio's brand constants (name, tagline, palette, links) in one place so an
app never re-types them.

MIT licensed, public.

## Getting started

```powershell
git clone https://github.com/0z00z0/0z0-shared.git
```

Consuming apps reference this repo by relative path, so it is cloned as a **sibling** of the
consuming app's own checkout — `..\0z0-shared` from the consumer's project directory. See
[Integrating the About dialogue](#integrating-the-about-dialogue) for the reference and CI recipe.

Requirements:

- **.NET 10 SDK.**
- **Windows 10 1809 (build 10.0.17763) or later**, with the Windows App SDK. The Windows App SDK
  and SDK build tools arrive as NuGet packages, so `dotnet restore` is enough.

`ZeroZero.Brand.WinUI` and the test harness target `net10.0-windows10.0.26100.0`, so the solution
builds on Windows only. `ZeroZero.Brand.Core` is plain `net10.0` and is portable in isolation.

## Projects

### `src/ZeroZero.Brand.Core`

Plain `net10.0` — no WinUI, no Windows-specific dependencies, safe to reference from a console
app or any other .NET target. Contains:

- **`Brand.cs`** — studio-wide constants: name, tagline, website, Buy Me a Coffee URL, GitHub org
  URL, and the brand palette as hex strings (teal / blue / purple / indigo / amber, plus the two
  background tones).
- **`ExternalLibrary.cs`** — a small record describing a third-party dependency to credit
  (name, author, purpose, licence, optional URL).
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
  centred on the monitor under the cursor, no title bar, always-on-top). A thin shell hosting
  `BrandAboutControl` plus the tray-app-only "Check for Updates" button. Carries its own minimal
  Win32 P/Invoke for monitor/DPI metrics, so it has no dependency on a consuming app's own
  `NativeMethods` class.
- **`BrandAboutOptions`** — the parameters: an `AboutInfo`, an optional `OnCheckForUpdates`
  callback (omit it to hide the "Check for Updates" button entirely — a console-only tool or a
  build without an update channel just doesn't pass one), and an optional `OnBeforeExit` hook for
  apps that need to self-exit cleanly before an installer-triggered relaunch.

Deliberately **not** shared: each app's own update-check networking/dialogue plumbing
(`UpdateCheckService`, `UpdateChecker`, `UpdatePrompt`, etc.). Only the window chrome and layout
are unified — `OnCheckForUpdates` is a plain `Func<Task<bool>>` the consumer wires up to its own
existing update flow (returning `true` when an update was applied so the window owns the
clean-exit-before-relaunch step via `OnBeforeExit`).

### `src/ZeroZero.Mqtt.WinUI`

`net10.0-windows10.0.26100.0`, WinUI 3 / Windows App SDK, unpackaged. References `ZeroZero.Mqtt`
for the protocol module and `ZeroZero.Brand.WinUI` for `InfoIcon`. Contains:

- **`MqttSettingsPanel`** — the settings page for the MQTT module: a master switch, a live status
  block, the device identity, a staged broker block behind an Apply, and one row per
  application-declared publish group. The panel renders the structure and knows no application's
  subject matter; everything domain-shaped arrives through `MqttPanelSetup` and every edit reports
  back as a callback.
- **`MqttPanelSetup`** — everything the panel needs from its host, in one object initialiser.
- **`MqttResourceStrings`** — the module's own `.resw`, read through `ResourceLoader`, with the
  built-in en-GB in `MqttStrings` as the floor.
- **`Themes/MqttPanelResources.xaml`** — five theme keys a host may override, defaulting to the
  stock WinUI theme.

[`consume-mqtt-settings-panel.md`](consume-mqtt-settings-panel.md) is the adoption checklist.

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

#### Scripts

Two PowerShell scripts in the repo root drive that harness:

- **`Show live 'About' dialogue.ps1`** — builds the harness if its exe is missing, then launches
  it, so both windows can be inspected on screen.
- **`Capture 'About' screenshot.ps1`** — launches the harness and writes window-only PNGs of both
  scenarios into `docs/screenshots/`: `about-window.png` (the popup) and `about-hosted-control.png`
  (the hosted control), the two images this README embeds. Capture goes through `PrintWindow` with
  `PW_RENDERFULLCONTENT`, so the translucent Mica backdrop resolves cleanly and no desktop content
  bleeds through; the two windows are told apart by their `AppWindow` title, not creation order.
- **`Capture 'MQTT panel' screenshots.ps1`** — launches the harness with `--mqtt` and writes the
  eight panel PNGs: each theme as the panel opens, with the Broker group open, with the publish list
  open, and holding an unapplied edit. It holds the display awake and checks the desktop is
  composing first, because DWM composes nothing while the display is off and a capture taken then is
  uniformly black.

The harness takes `--mqtt` to open the MQTT panel scenario instead of the About windows: one
component per run, so unrelated windows never land on top of each other.

## Screenshots

**`BrandAboutWindow`** (tray-app popup):

![BrandAboutWindow](docs/screenshots/about-window.png)

**`BrandAboutControl`** hosted directly in a plain window (no popup chrome, no update button):

![BrandAboutControl hosted](docs/screenshots/about-hosted-control.png)

**`MqttSettingsPanel`**, as it opens (light and dark):

| Light | Dark |
|---|---|
| ![MQTT panel, light](docs/screenshots/mqtt-panel-light.png) | ![MQTT panel, dark](docs/screenshots/mqtt-panel-dark.png) |

With the Broker group open, and with the publish list open:

| Broker | Publish groups |
|---|---|
| ![MQTT panel, broker group open](docs/screenshots/mqtt-panel-light-broker.png) | ![MQTT panel, publish list open](docs/screenshots/mqtt-panel-light-groups.png) |

An unapplied broker edit is marked beside the section heading, so a closed group cannot hide it:

![MQTT panel, unapplied edit](docs/screenshots/mqtt-panel-light-edited.png)

All images are the capture scripts' output, so they show the surfaces as they actually render
rather than what the XAML claims.

## Integrating the About dialogue

### 1. Reference the library

There is no NuGet feed yet — tracked in
[issue #1](https://github.com/0z00z0/0z0-shared/issues/1) — so a consumer takes a
`ProjectReference` on a checkout of this repo. Route it through an MSBuild property that defaults
to the sibling folder, so CI can point the same reference somewhere else without editing the
`.csproj`:

```xml
<PropertyGroup>
  <ZeroZeroSharedDir Condition="'$(ZeroZeroSharedDir)' == ''">..\0z0-shared</ZeroZeroSharedDir>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="$(ZeroZeroSharedDir)\src\ZeroZero.Brand.WinUI\ZeroZero.Brand.WinUI.csproj">
    <UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>
  </ProjectReference>
</ItemGroup>
```

`UndefineProperties="WindowsAppSDKSelfContained"` is required whenever the consuming app publishes
self-contained with that property set globally (on the command line, for instance): MSBuild
propagates a global property into every project reference, and the Windows App SDK targets reject
it on a class library — *"should not be applied to a class library"*. Stripping it for this
reference only lets the app aggregate the self-contained runtime while the library builds
framework-dependent. An app that instead declares `WindowsAppSDKSelfContained` as a project-level
property never propagates it and does not need the metadata.

`ZeroZero.Brand.WinUI` pulls in `ZeroZero.Brand.Core` transitively and ships the brand typeface
(Cascadia Mono) as content, so a consumer gets the correct font with no extra setup. The consuming
app's `app.manifest` declares `PerMonitorV2` DPI awareness (as ChargeKeeper and HyperVManagerTray
do) so the window renders sharp on high-DPI displays.

### 2. Make the reference resolve in CI

A GitHub Actions runner checks out one repo, so the consumer's workflow has to fetch this one as
well. Two working shapes, both in use:

**Workspace subfolder + property override.** `actions/checkout` refuses a `path:` outside the
workspace, so the second checkout lands *inside* it and the `ZeroZeroSharedDir` property is
redirected there through job-level `env` (MSBuild reads environment variables as properties):

```yaml
jobs:
  build-test:
    runs-on: windows-latest
    env:
      ZeroZeroSharedDir: ${{ github.workspace }}/0z0-shared
    steps:
      - uses: actions/checkout@v7
      - uses: actions/checkout@v7
        with:
          repository: 0z00z0/0z0-shared
          path: 0z0-shared
```

The checkout then sits under the consuming project's own directory, where the SDK's default globs
would compile this repo's sources into the consuming assembly on top of the `ProjectReference` —
duplicate types and an ambiguous `NativeMethods`. Exclude the folder from the consuming project's
item globs:

```xml
<ItemGroup>
  <Compile               Remove="0z0-shared\**\*" />
  <Content               Remove="0z0-shared\**\*" />
  <None                  Remove="0z0-shared\**\*" />
  <Page                  Remove="0z0-shared\**\*" />
  <ApplicationDefinition Remove="0z0-shared\**\*" />
  <EmbeddedResource      Remove="0z0-shared\**\*" />
</ItemGroup>
```

That matches nothing locally, where the repo is an outside-the-tree sibling.

**Clone to a real sibling.** The alternative keeps the relative path identical to local dev by
cloning (this repo is public, so no token is needed) beside the workspace checkout, and is what a
pinned build wants because `checkout --detach` takes an exact commit:

```yaml
      - name: Clone 0z0-shared (sibling dependency, pinned)
        shell: pwsh
        run: |
          git clone https://github.com/0z00z0/0z0-shared.git ../0z0-shared
          git -C ../0z0-shared checkout --detach $ref
```

### 3. Pin the version, if reproducibility matters

Local dev builds against the live sibling checkout while CI builds a pinned commit, so a consumer
that adopts a newly added shared type builds green locally and fails CI with `CS0234`. A consumer
that wants reproducible builds therefore keeps two things: a **pinned-SHA file** read by every
workflow that clones this repo (one file, so the pins cannot drift between CI and release), and a
**build-time drift guard** — an MSBuild target that compares the live sibling checkout against that
file and raises a warning, never an error, and skips entirely when either the ref file or the
sibling clone is absent. ChargeKeeper's `.github/0z0-shared-ref` plus its `CheckSharedPin` target
and `scripts/check-shared-pin.ps1` are the working example.

### 4. Pick the hosting style

Both share the same `AboutInfo` data model — the choice is whether the consuming app has a separate
About *window* or an About *page*:

| | Tray/systray apps | Full windowed apps |
|---|---|---|
| Component | `BrandAboutWindow` | `BrandAboutControl` |
| Surface | Standalone popup (Mica, no title bar, always-on-top) | Hosted inside the app's own `Page`/window |
| "Check for Updates" | Yes, via `BrandAboutOptions` | No — not this layer's concern |
| Used by | ChargeKeeper, HyperVManagerTray | (candidate: M365Migrator) |

### Option A — Tray app popup (`BrandAboutWindow`)

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
  **Omit it entirely to hide the "Check for Updates" button** — e.g. a console-only tool or a build
  with no update channel.
- **`OnBeforeExit`** (`Func<Task<bool>>`) — run just before an update-triggered close so the app
  can tear down cleanly; return `false` to veto the exit and keep the window open.

The window owns only chrome and layout; each app keeps its own update-check networking/dialogue
plumbing and wires it in through these two callbacks.

### Option B — Hosted in the app's own page (`BrandAboutControl`)

A full windowed app whose About is an in-navigation `Page` (not a separate popup, and with no
"check for updates" concept) skips `BrandAboutWindow` entirely and hosts the content control itself.

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
repo's `AboutInfo` — e.g. a `BrandInfo` static class that also feeds a CLI banner):

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

`SetInfo` is a method rather than a settable property (WinUI's XAML compiler needs a parameterless
constructor for any type exposed as a public property on a XAML class, which `AboutInfo`'s
`required` members deliberately don't have) — **call it exactly once, from the hosting page's
constructor or its `Loaded` handler**, after `InitializeComponent`.

**3. Delete the bespoke About view-model/layout** once the control renders correctly; keeping both
is what lets them drift. The app's own brand-facts class (`BrandInfo` or equivalent) stays as the
single source of truth — only its *rendering* moves to the shared control, not its data.

**Notes:**
- The control inherits the host page's theme (everything but the fixed-colour brand header band
  uses `ThemeResource` brushes), so no extra theming work is needed.
- Never shows an update button — there's no `BrandAboutOptions` and no update-flow concept at this
  layer. An app that does need an update check on its About surface is a case for
  `BrandAboutWindow` instead (Option A).
- The control supplies the `[Ø]` studio mark, the company name and the tagline itself, from
  `Brand`'s studio-wide constants. Of the three link buttons — **Repository / Website / Donate** —
  only `RepoUrl` comes from the `AboutInfo`; Website and Donate always point at the studio's own
  `Brand.WebsiteUrl` / `Brand.BuyMeACoffeeUrl` rather than anything per-app. None of those five are
  supplied by the consumer.
- The reference and CI recipe above applies here too: the consuming app's own workflow needs one of
  the two checkout shapes, or a NuGet pin once
  [issue #1](https://github.com/0z00z0/0z0-shared/issues/1) lands.

## Package versions

The library pins `Microsoft.WindowsAppSDK` `2.2.0` and `Microsoft.Windows.SDK.BuildTools`
`10.0.28000.2270`. Those pins are a floor, not a lock: a consuming app may pin higher, and NuGet
unifies a package graph on the version nearest the consuming project, so the app's own pin governs
the Windows App SDK runtime that is actually resolved for the whole build.

## Conventions

[`docs/TODO-HANDLING.md`](docs/TODO-HANDLING.md) is the studio-wide work-tracking convention every
0z0 repo follows: GitHub Issues are the source of truth, and a git-ignored local `TODO.md` mirrors
them.

## Build

```powershell
dotnet build 0z0-shared.slnx
```

## Licence

[MIT](LICENSE) © ZeroZero Software ([0z0.xyz](https://0z0.xyz))
