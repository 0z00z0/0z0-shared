# ZeroZero Software — shared library

The building blocks ZeroZero Software's desktop apps share, so no app re-types them. Published as
packages from this repository — one package per assembly, and one for the build kit, which carries
no assembly, each versioned and released with its component — and consumable equally from a
sibling checkout. MIT licensed, public.

## Components

A **component** is the unit of adoption: one to three projects, one entry-point reference, one
guide, its own version and its own release tag. A **foundation** assembly is one any component may
take — no domain vocabulary and no dependencies of its own — and is never part of a component. An
assembly belongs to the component its name's first segment after `ZeroZero.` spells, so a new
assembly's name is chosen against the keys below; [`docs/releasing.md`](docs/releasing.md) has the
rule.

| Component | Key | Packages | What it is | Guide |
|---|---|---|---|---|
| Brand | `brand` | `ZeroZero.Brand.Core`, `ZeroZero.Brand.WinUI` | The studio's identity constants, the About control and the About window, the brand typeface, and the palette as a resource dictionary XAML can merge. Entry point `ZeroZero.Brand.WinUI`; a console tool takes `ZeroZero.Brand.Core` alone. | [`docs/zerozero-brand.md`](docs/zerozero-brand.md) |
| Diagnostics | `diagnostics` | `ZeroZero.Diagnostics`, `ZeroZero.Diagnostics.Dumps` | Crash diagnostics: the process-wide unhandled-exception arms routed to one place, a crash-line file that never throws, the startup version line, and the Windows Error Reporting dump registration with its lifecycle. Logging configuration stays with the application. Entry point `ZeroZero.Diagnostics`; a consumer wanting the registration alone takes `ZeroZero.Diagnostics.Dumps`. | [`docs/zerozero-diagnostics.md`](docs/zerozero-diagnostics.md) |
| Lifecycle | `lifecycle` | `ZeroZero.Lifecycle` | The single-instance lock held for the life of the process, the deliberate-exit mark, relaunch on any other clean exit under a three-in-ten-minutes limit, and the per-user data path. Entry point `ZeroZero.Lifecycle`; no user interface. | [`docs/zerozero-lifecycle.md`](docs/zerozero-lifecycle.md) |
| MQTT | `mqtt` | `ZeroZero.Mqtt`, `ZeroZero.Mqtt.Discovery`, `ZeroZero.Mqtt.WinUI` | An MQTT 5.0 connection, the device document that puts an application into a discovery-aware receiver as one device with entities, and the settings panel a host embeds. Entry point `ZeroZero.Mqtt.WinUI`; a headless consumer takes `ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` and pulls in no WinUI. | [`docs/zerozero-mqtt.md`](docs/zerozero-mqtt.md) |
| Startup | `startup` | `ZeroZero.Startup` | The application's logon task in the Task Scheduler: its identity, the power-safe elevated definition, registration, the direct enabled read, enable, disable, delete, repair of a task an older build registered, and a demand start that proves the task runs. The manifest, the installer and the watchdog task stay with the application. Entry point `ZeroZero.Startup`; no user interface. | [`docs/zerozero-startup.md`](docs/zerozero-startup.md) |

| Foundation | Key | Package | What it is | Guide |
|---|---|---|---|---|
| Config | `config` | `ZeroZero.Config` | Atomic JSON settings files: typed snapshot reads, mutation under one lock, change notification, quarantine of a file that cannot be parsed. The MQTT module stores its settings and its discovery ledger through it. | [`docs/zerozero-config.md`](docs/zerozero-config.md) |
| Controls | `controls` | `ZeroZero.Controls.WinUI` | WinUI controls with no studio identity: the settings-row vocabulary — info bubble, section header, card row — title-bar theming, and the single-line text prompt. The one WinUI foundation assembly, so a UI component takes it without the brand's font pack and About window. The MQTT panel puts a bubble on every row. | [`docs/zerozero-controls.md`](docs/zerozero-controls.md) |
| Primitives | `primitives` | `ZeroZero.Primitives` | The two-member log sink and its no-op, the reader of the version an assembly reports with its About-box form, the coalescing gate, and the source-revision stamp as build properties and targets. The MQTT module writes to the sink and runs every retained channel on the gate. | [`docs/zerozero-primitives.md`](docs/zerozero-primitives.md) |
| Tray | `tray` | `ZeroZero.Tray` | The tray icon's container and sizing policy: the PNG-in-ICO file writer, the slot size at the taskbar's own scale, and whether the taskbar is light or dark with the stroke tone that reads on it. Headless, no drawing; the WinUI host of the icon is a later project under the same key. | [`docs/zerozero-tray.md`](docs/zerozero-tray.md) |
| Win32 | `win32` | `ZeroZero.Win32` | The raw Win32 layer: monitor, DPI and taskbar metrics as plain numbers, the native task dialog and message boxes, dark native chrome. Headless, so a console tool can take it; the About window and the text prompt take their monitor metrics from here. | [`docs/zerozero-win32.md`](docs/zerozero-win32.md) |

| Build machinery | Key | Package | What it is | Guide |
|---|---|---|---|---|
| Build | `build` | `ZeroZero.Build` | The build kit every repository in the family shares: the language and studio-identity property blocks, the unpackaged WinUI application block, the application manifest as a token-substituted template, the signing script with its publish-time target, and the third-party pins under central package management. No assembly; taken as an MSBuild SDK or by path, never as a package reference. This repository builds under it. | [`docs/zerozero-build.md`](docs/zerozero-build.md) |

Dependencies point downward: a component's projects take foundation and each other, and never
another component; a foundation assembly may take another foundation assembly beneath it. The
MQTT projects take `ZeroZero.Primitives` and `ZeroZero.Config`, the MQTT panel takes
`ZeroZero.Controls.WinUI` for the info bubble, the About window takes `ZeroZero.Win32`,
`ZeroZero.Controls.WinUI` and `ZeroZero.Tray` take `ZeroZero.Win32` for their monitor and taskbar
metrics, and the diagnostics, lifecycle and startup components take `ZeroZero.Primitives` — for
the log sink, and in diagnostics for the version reader as well. Taking the MQTT module therefore
brings four foundation assemblies with it and no brand assembly; an application that wants the
About control takes the brand component as well.

Third-party packages are the Windows App SDK, the Community Toolkit's settings controls,
**MQTTnet**, the **TaskScheduler** library and **Microsoft.Win32.SystemEvents** — the last three
confined to `ZeroZero.Mqtt`, `ZeroZero.Startup` and `ZeroZero.Lifecycle` in turn, where no type of
any of them reaches a public signature. `ZeroZero.Brand.Core`, `ZeroZero.Config`,
`ZeroZero.Primitives` and `ZeroZero.Win32` reference nothing at all; the diagnostics assemblies
and `ZeroZero.Tray` reference a foundation assembly and no package; `ZeroZero.Controls.WinUI`
references the Windows App SDK and the toolkit, and no toolkit type reaches its public signature.
Every third-party version is pinned once, in the build kit's `ZeroZero.Packages.props`, which this
repository's `Directory.Packages.props` imports and every consuming repository imports the same
way.

`ZeroZero.Brand.WinUI.TestHarness` is an interactive exe that opens either UI surface, the brand
palette, the settings rows, the title bars, the text prompt, or the native dialogs, on screen from
fabricated state; it is never packed and nothing references it. It builds under the kit's WinUI
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
plain `net10.0` assemblies and every test project but five are portable in isolation.
`ZeroZero.Win32.Tests` calls user32, `ZeroZero.Diagnostics.Dumps.Tests` writes the registry,
`ZeroZero.Lifecycle.Tests` takes named mutexes and starts a child process, `ZeroZero.Startup.Tests`
registers tasks in the real Task Scheduler and `ZeroZero.Tray.Tests` reads the taskbar window and
the personalisation key, so those five run on Windows only. The MQTT module additionally needs an
MQTT 5.0 broker at run time, and Home Assistant 2024.11.0 or later for discovery.

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
components are not yet on the feed. [`docs/releasing.md`](docs/releasing.md) is the procedure.

## Documentation

| Document | Covers |
|---|---|
| [`docs/consuming.md`](docs/consuming.md) | Both reference routes, the CI shapes, pinning per component, the traps, the third-party pins. |
| [`docs/zerozero-brand.md`](docs/zerozero-brand.md) | The brand component: the assemblies, the two hosting styles, the screenshots, the harness. |
| [`docs/zerozero-diagnostics.md`](docs/zerozero-diagnostics.md) | The diagnostics component: the crash handlers, the crash line, the version line, the dump registration and its lifecycle, the wiring order, and what stays with the application. |
| [`docs/zerozero-mqtt.md`](docs/zerozero-mqtt.md) | The MQTT module end to end: the assemblies, the six wiring steps, the entity model, identity, the encryption model and the panel. |
| [`docs/zerozero-config.md`](docs/zerozero-config.md) | The config foundation assembly. |
| [`docs/zerozero-controls.md`](docs/zerozero-controls.md) | The controls foundation assembly: the settings-row vocabulary, title-bar theming, the text prompt, and how the harness shows each. |
| [`docs/zerozero-primitives.md`](docs/zerozero-primitives.md) | The primitives foundation assembly: the log sink, the version reader, the coalescing gate and the source-revision stamp. |
| [`docs/zerozero-tray.md`](docs/zerozero-tray.md) | The tray foundation assembly: the icon file writer, the slot size at the taskbar's scale, and the taskbar's theme. |
| [`docs/zerozero-win32.md`](docs/zerozero-win32.md) | The Win32 foundation assembly, and the manifest dependency its task dialog needs. |
| [`docs/zerozero-lifecycle.md`](docs/zerozero-lifecycle.md) | The lifecycle component: the lock, the relaunch and its limit, the data path, the wiring order and its traps. |
| [`docs/zerozero-startup.md`](docs/zerozero-startup.md) | The startup component: the logon task, its definition and repair, what stays with the application, and the token it needs. |
| [`docs/zerozero-build.md`](docs/zerozero-build.md) | The build kit: the shared property blocks, the WinUI application block, the manifest template, signing, the pin rule and its guards, and how a repository takes it on either route. |
| [`docs/consume-brand-about-control.md`](docs/consume-brand-about-control.md) | `BrandAboutControl`, as an adoption checklist. |
| [`docs/consume-mqtt-settings-panel.md`](docs/consume-mqtt-settings-panel.md) | The panel alone, as an adoption checklist. |
| [`docs/releasing.md`](docs/releasing.md) | Cutting a component release, what the workflow guards, and how to run the guards locally. |
| [`docs/release-notes/`](docs/release-notes) | One file per tag, under the component's folder from `0.7.0` and at the folder root for the earlier tags that released everything together. What a release contains and what it breaks. |
| [`docs/TODO-HANDLING.md`](docs/TODO-HANDLING.md) | The studio-wide work-tracking convention every 0z0 repo follows: GitHub Issues are the source of truth, and a git-ignored local `TODO.md` mirrors them. |

## Licence

[MIT](LICENSE) © ZeroZero Software ([0z0.xyz](https://0z0.xyz))
