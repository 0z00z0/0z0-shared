# ZeroZero Software — shared library

The building blocks ZeroZero Software's desktop apps share, so no app re-types them. Published as
packages from this repository — one package per assembly, and one for the build kit, which carries
no assembly, each versioned and released with its component — and consumable equally from a
sibling checkout. MIT licensed, public.

## Components

A **component** is the unit of adoption: one to three projects, one entry-point reference, one
guide, its own version and its own release tag. A **foundation** assembly is one any component may
take — no domain vocabulary, and no dependency of its own beyond another foundation assembly
beneath it — and is never part of a component. An
assembly belongs to the component its name's first segment after `ZeroZero.` spells, so a new
assembly's name is chosen against the keys below; [`docs/releasing.md`](docs/releasing.md) has the
rule.

| Component | Key | Packages | What it is | Guide |
|---|---|---|---|---|
| Brand | `brand` | `ZeroZero.Brand.Core`, `ZeroZero.Brand.WinUI` | The studio's identity constants, the About control and the About window, the brand typeface, and the palette as a resource dictionary XAML can merge. Entry point `ZeroZero.Brand.WinUI`; a console tool takes `ZeroZero.Brand.Core` alone. | [`docs/zerozero-brand.md`](docs/zerozero-brand.md) |
| Diagnostics | `diagnostics` | `ZeroZero.Diagnostics`, `ZeroZero.Diagnostics.Dumps` | Crash diagnostics: the process-wide unhandled-exception arms routed to one place, a crash-line file that never throws, the startup version line, and the Windows Error Reporting dump registration with its lifecycle. Logging configuration stays with the application. Entry point `ZeroZero.Diagnostics`; a consumer wanting the registration alone takes `ZeroZero.Diagnostics.Dumps`. | [`docs/zerozero-diagnostics.md`](docs/zerozero-diagnostics.md) |
| Lifecycle | `lifecycle` | `ZeroZero.Lifecycle` | The single-instance lock held for the life of the process, the deliberate-exit mark, relaunch on any other clean exit under a three-in-ten-minutes limit, and the per-user data path. Entry point `ZeroZero.Lifecycle`; no user interface. | [`docs/zerozero-lifecycle.md`](docs/zerozero-lifecycle.md) |
| MQTT | `mqtt` | `ZeroZero.Mqtt`, `ZeroZero.Mqtt.Discovery`, `ZeroZero.Mqtt.WinUI` | An MQTT 5.0 connection, the device document that puts an application into a discovery-aware receiver as one device with entities, and the settings panel a host embeds. Entry point `ZeroZero.Mqtt.WinUI`; a headless consumer takes `ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` and pulls in no WinUI. | [`docs/zerozero-mqtt.md`](docs/zerozero-mqtt.md) |
| Settings shell | `settingsshell` | `ZeroZero.SettingsShell.WinUI` | The settings window with every page left to the application: Mica chrome with the title bar painted for the theme, a navigation pane with a product footer, one scroll viewer over the pages, placement against the application's saved rectangle, Escape to close, and a section lifecycle with enter and leave hooks and a per-section build-once flag. Entry point `ZeroZero.SettingsShell.WinUI`. | [`docs/zerozero-settingsshell.md`](docs/zerozero-settingsshell.md) |
| Startup | `startup` | `ZeroZero.Startup` | The application's logon task in the Task Scheduler: its identity, the power-safe elevated definition, registration, the direct enabled read, enable, disable, delete, repair of a task an older build registered, and a demand start that proves the task runs. The manifest, the installer and the watchdog task stay with the application. Entry point `ZeroZero.Startup`; no user interface. | [`docs/zerozero-startup.md`](docs/zerozero-startup.md) |
| Tray | `tray` | `ZeroZero.Tray`, `ZeroZero.Tray.WinUI` | The tray icon: the host that creates the icon with the notify-icon library's efficiency mode refused, follows the taskbar's theme and display and the shell's restarts, keeps the rendered file in a cache, holds the tooltip to the shell's limit, classifies clicks and rebuilds the menu before it opens; and the plain half — the PNG-in-ICO file writer, the slot size at the taskbar's own scale, and whether the taskbar is light or dark with the stroke tone that reads on it. The drawing and the notifications stay with the application. Entry point `ZeroZero.Tray.WinUI`; a headless renderer takes `ZeroZero.Tray`. | [`docs/zerozero-tray.md`](docs/zerozero-tray.md) |
| Update | `update` | `ZeroZero.Update`, `ZeroZero.Update.Win32` | The update flow: the latest GitHub release against the running version, the download into a fresh private directory, verification of the installer before it runs — its Authenticode signature and publisher against the expected signer, and its SHA-256 against the hash the release publishes — the launch and the hand-over to the application's own shutdown, the stale-download sweep and the check scheduler; and the update dialogs, worded here and marshalled by the Win32 foundation. The options, the installer and when the application exits stay with the application. Entry point `ZeroZero.Update.Win32`; a headless consumer takes `ZeroZero.Update`. | [`docs/zerozero-update.md`](docs/zerozero-update.md) |

| Foundation | Key | Package | What it is | Guide |
|---|---|---|---|---|
| Config | `config` | `ZeroZero.Config`, `ZeroZero.Config.Sections` | Settings on disk. The plain assembly is one JSON file holding one type: typed snapshot reads, mutation under one lock, a write that lands whole or not at all, change notification, quarantine of a file that cannot be parsed. The sections assembly is one document whose top-level keys are sections owned by different components, addressed one at a time — a sibling section, a section from a build that no longer exists and a hand-written comment all survive a write untouched, and no write ever adds a comment or a second key differing from another only in case — plus the migration from an older file, which writes the new one and leaves the old one alone. The MQTT module stores its settings and its discovery ledger through the plain assembly. Entry point `ZeroZero.Config.Sections`; a consumer whose file holds one type takes `ZeroZero.Config`. | [`docs/zerozero-config.md`](docs/zerozero-config.md) |
| Controls | `controls` | `ZeroZero.Controls.WinUI` | WinUI controls with no studio identity: the settings-row vocabulary — info bubble, section header, card row — title-bar theming, and the single-line text prompt. The one WinUI foundation assembly, so a UI component takes it without the brand's font pack and About window. The MQTT panel puts a bubble on every row. | [`docs/zerozero-controls.md`](docs/zerozero-controls.md) |
| Primitives | `primitives` | `ZeroZero.Primitives` | The two-member log sink and its no-op, the reader of the version an assembly reports with its About-box form, the coalescing gate, and the source-revision stamp as build properties and targets. The MQTT module writes to the sink and runs every retained channel on the gate. | [`docs/zerozero-primitives.md`](docs/zerozero-primitives.md) |
| Win32 | `win32` | `ZeroZero.Win32` | The raw Win32 layer: monitor, DPI and taskbar metrics as plain numbers, the native task dialog and message boxes, dark native chrome. Headless, so a console tool can take it; the About window and the text prompt take their monitor metrics from here. | [`docs/zerozero-win32.md`](docs/zerozero-win32.md) |

| Build machinery | Key | Package | What it is | Guide |
|---|---|---|---|---|
| Build | `build` | `ZeroZero.Build` | The build kit every repository in the family shares: the language and studio-identity property blocks, the unpackaged WinUI application block, the application manifest as a token-substituted template, the signing script with its publish-time target, and the third-party pins under central package management. No assembly; taken as an MSBuild SDK or by path, never as a package reference. This repository builds under it. | [`docs/zerozero-build.md`](docs/zerozero-build.md) |

Dependencies point downward: a component's projects take foundation and each other, and never
another component; a foundation assembly may take another foundation assembly beneath it. The
MQTT projects take `ZeroZero.Primitives` and `ZeroZero.Config`, the MQTT panel takes
`ZeroZero.Controls.WinUI` for the info bubble, the settings shell takes `ZeroZero.Controls.WinUI`
for the title bar and the monitor metrics beneath it, the About window takes `ZeroZero.Win32`,
`ZeroZero.Controls.WinUI` and `ZeroZero.Tray` take `ZeroZero.Win32` for their monitor and taskbar
metrics, the tray host takes `ZeroZero.Tray` for the icon file, the slot and the theme, the update
dialog project takes `ZeroZero.Win32` for the task dialog and the message
boxes, and the diagnostics, lifecycle, startup and update components take `ZeroZero.Primitives` —
for the log sink, and in diagnostics and update for the version reader as well. Taking the MQTT
module therefore brings four foundation assemblies with it and no brand assembly; an application
that wants the About control takes the brand component as well.

Third-party packages are the Windows App SDK, the Community Toolkit's settings controls, the
**H.NotifyIcon** notify-icon library, **MQTTnet**, the **TaskScheduler** library and
**Microsoft.Win32.SystemEvents** — the notify-icon library confined to `ZeroZero.Tray.WinUI`,
MQTTnet to `ZeroZero.Mqtt`, the scheduler library to `ZeroZero.Startup`, and SystemEvents to
`ZeroZero.Lifecycle` and `ZeroZero.Tray.WinUI`, where no type of any of them reaches a public
signature. `ZeroZero.Brand.Core`, `ZeroZero.Config`, `ZeroZero.Primitives` and `ZeroZero.Win32`
reference nothing at all, and `ZeroZero.Config.Sections` references only `ZeroZero.Config` beneath
it; the diagnostics assemblies, the update assemblies and `ZeroZero.Tray`
reference foundation assemblies and no package; `ZeroZero.Controls.WinUI` references the Windows
App SDK and the toolkit, and no toolkit type reaches its public signature;
`ZeroZero.SettingsShell.WinUI` references the Windows App SDK and the controls foundation
assembly, and takes the toolkit only through it.
Every third-party version is pinned once, in the build kit's `ZeroZero.Packages.props`, which this
repository's `Directory.Packages.props` imports and every consuming repository imports the same
way.

`ZeroZero.Brand.WinUI.TestHarness` is an interactive exe that opens either UI surface, the brand
palette, the settings rows, the title bars, the text prompt, the settings window shell, the native
dialogs, or the tray icon with its tooltip and menu, on screen from fabricated state; it is never
packed and nothing references it, though the tray tests start it as a child process to measure
the host. It builds under the kit's WinUI
application block, the one project in the repository that does, so the block and the manifest
writer are exercised by every build here. The capture and demo scripts that drive it are under
`scripts/`.

## Consuming

Two routes, both supported: a `PackageReference` on the studio's GitHub Packages feed at
`https://nuget.pkg.github.com/0z00z0/index.json` — **which authenticates every read even though the
packages are public**, so a restore needs a token with `read:packages` — or a `ProjectReference` on
a sibling checkout, which needs no credential at all. One reference per adopted component, one route
per consuming repository. [`docs/consuming.md`](docs/consuming.md) carries both routes, the CI
shapes, pinning and the traps; each component's guide adds its own wiring on top.

## Requirements

- **.NET 10 SDK.**
- **Windows 10 1809 (build 10.0.17763) or later**, with the Windows App SDK, for the WinUI
  assemblies. The Windows App SDK and SDK build tools arrive as NuGet packages, so `dotnet restore`
  is enough.

The WinUI projects target `net10.0-windows10.0.26100.0`, so the solution builds on Windows only; the
plain `net10.0` assemblies and every test project but seven are portable in isolation.
`ZeroZero.Win32.Tests` calls user32, `ZeroZero.Diagnostics.Dumps.Tests` writes the registry,
`ZeroZero.Lifecycle.Tests` takes named mutexes and starts a child process, `ZeroZero.Startup.Tests`
registers tasks in the real Task Scheduler, `ZeroZero.Tray.Tests` reads the taskbar window and the
personalisation key, `ZeroZero.Update.Tests` calls wintrust and signs files through PowerShell, and
`ZeroZero.ReleaseVerification.Tests` reads Authenticode signatures through PowerShell, so those
seven run on Windows only. The MQTT module additionally needs an MQTT 5.0 broker at run time, and
Home Assistant 2024.11.0 or later for discovery.

## Build and test

```powershell
git clone https://github.com/0z00z0/0z0-shared.git
dotnet build 0z0-shared.slnx -c Release
.\.github\scripts\run-tests.ps1
```

The script is the test definition CI and the release workflow share: it discovers every project
under `tests/`, runs each against the Release build, and fails naming every project that failed.

## Releasing

Each component carries its own version in `Versions.props` and releases under its own tag,
`<key>-v<x.y.z>`, with notes under `docs/release-notes/<key>/`. The release workflow refuses a tag
that disagrees with the declared version, has no notes, or names a component whose referenced
components are not yet on the feed. After the push, a job of its own fetches every package back
from the feed and asserts it is the one this run packed — the bytes, the nuspec's commit and every
assembly's stamp — and the release page is created only when that passes; the same check is a
reusable workflow the applications call, with a signing gate and a manifest rewriter beside it.
[`docs/releasing.md`](docs/releasing.md) is the procedure.

## Documentation

| Document | Covers |
|---|---|
| [`docs/consuming.md`](docs/consuming.md) | Both reference routes, the CI shapes, pinning per component, the traps, the third-party pins. |
| [`docs/zerozero-brand.md`](docs/zerozero-brand.md) | The brand component: the assemblies, the two hosting styles, the screenshots, the harness. |
| [`docs/zerozero-diagnostics.md`](docs/zerozero-diagnostics.md) | The diagnostics component: the crash handlers, the crash line, the version line, the dump registration and its lifecycle, the wiring order, and what stays with the application. |
| [`docs/zerozero-mqtt.md`](docs/zerozero-mqtt.md) | The MQTT module end to end: the assemblies, the six wiring steps, the entity model, identity, the encryption model and the panel. |
| [`docs/zerozero-config.md`](docs/zerozero-config.md) | The config foundation assemblies: the single-type settings file and its atomic write, the section-addressed document and what a write does and does not touch, and the migration. |
| [`docs/zerozero-controls.md`](docs/zerozero-controls.md) | The controls foundation assembly: the settings-row vocabulary, title-bar theming, the text prompt, and how the harness shows each. |
| [`docs/zerozero-primitives.md`](docs/zerozero-primitives.md) | The primitives foundation assembly: the log sink, the version reader, the coalescing gate and the source-revision stamp. |
| [`docs/zerozero-settingsshell.md`](docs/zerozero-settingsshell.md) | The settings shell component: the division between shell and pages, the section lifecycle, placement, theming, the traps, and how the harness shows it. |
| [`docs/zerozero-tray.md`](docs/zerozero-tray.md) | The tray component: the host's lifecycle, listeners, cache, tooltip discipline, click classification and menu protocol, the wiring, what stays with the application, and the plain half — the icon file writer, the slot size at the taskbar's scale, and the taskbar's theme. |
| [`docs/zerozero-win32.md`](docs/zerozero-win32.md) | The Win32 foundation assembly, and the manifest dependency its task dialog needs. |
| [`docs/zerozero-lifecycle.md`](docs/zerozero-lifecycle.md) | The lifecycle component: the lock, the relaunch and its limit, the data path, the wiring order and its traps. |
| [`docs/zerozero-startup.md`](docs/zerozero-startup.md) | The startup component: the logon task, its definition and repair, what stays with the application, and the token it needs. |
| [`docs/zerozero-update.md`](docs/zerozero-update.md) | The update component: the two verification checks and why a checksum beside the file is not one of them, where the published hash comes from, the wiring, what stays with the application, and the traps. |
| [`docs/zerozero-build.md`](docs/zerozero-build.md) | The build kit: the shared property blocks, the WinUI application block, the manifest template, signing, the pin rule and its guards, and how a repository takes it on either route. |
| [`docs/consume-brand-about-control.md`](docs/consume-brand-about-control.md) | `BrandAboutControl`, as an adoption checklist. |
| [`docs/consume-mqtt-settings-panel.md`](docs/consume-mqtt-settings-panel.md) | The panel alone, as an adoption checklist. |
| [`docs/releasing.md`](docs/releasing.md) | Cutting a component release, what the workflow guards, and how to run the guards locally. |
| [`docs/release-notes/`](docs/release-notes) | One file per tag, under the component's folder from `0.7.0` and at the folder root for the earlier tags that released everything together. What a release contains and what it breaks. |
| [`docs/TODO-HANDLING.md`](docs/TODO-HANDLING.md) | The studio-wide work-tracking convention every 0z0 repo follows: GitHub Issues are the source of truth, and a git-ignored local `TODO.md` mirrors them. |

## Licence

[MIT](LICENSE) © ZeroZero Software ([0z0.xyz](https://0z0.xyz))
