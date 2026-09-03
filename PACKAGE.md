# ZeroZero Software — shared library

The building blocks ZeroZero Software's desktop apps share, published as packages from
[0z00z0/0z0-shared](https://github.com/0z00z0/0z0-shared) — one package per assembly, and one for
the build kit, which carries no assembly, each versioned and released with its component. MIT
licensed, public.

| Package | Component | Target | What it is |
|---|---|---|---|
| `ZeroZero.Brand.Core` | `brand` | `net10.0` | Brand constants and the About-window data contracts. |
| `ZeroZero.Brand.WinUI` | `brand` | `net10.0-windows10.0.26100.0` | The About window, the hosted About control, the brand typeface, and the palette as a resource dictionary. |
| `ZeroZero.Build` | `build` | none — an MSBuild SDK | The build kit: shared property blocks, the unpackaged WinUI application block, the manifest template, the signing script and the family's third-party pins under central package management. Taken through `global.json` and three `Sdk="ZeroZero.Build"` imports, never as a package reference. |
| `ZeroZero.Config` | `config` (foundation) | `net10.0` | Atomic JSON settings files: typed snapshot reads, mutation under one lock, change notification, quarantine of a file that cannot be parsed. |
| `ZeroZero.Controls.WinUI` | `controls` (foundation) | `net10.0-windows10.0.26100.0` | WinUI controls with no studio identity: the settings-row vocabulary — info bubble, section header, card row — title-bar theming, and the single-line text prompt. Takes `ZeroZero.Win32` and the Community Toolkit's settings controls. |
| `ZeroZero.Diagnostics` | `diagnostics` | `net10.0` | Crash diagnostics: the process-wide unhandled-exception arms routed to one place, a crash-line appender that never throws, and the startup version line. The component's entry point; carries the dump registration with it. |
| `ZeroZero.Diagnostics.Dumps` | `diagnostics` | `net10.0` | The Windows Error Reporting local dump registration with a lifecycle: arm, disarm, sweep older builds' registrations, remove the shared root once empty, prune old dump files. Windows only. |
| `ZeroZero.Lifecycle` | `lifecycle` | `net10.0` | The single-instance lock held for the life of the process, the deliberate-exit mark, relaunch on any other clean exit under a sliding-window limit, and the per-user data path. Windows only. |
| `ZeroZero.Mqtt` | `mqtt` | `net10.0` | An MQTT 5.0 connection for desktop applications: endpoint search and probe, retained per-entity channels, command routing, availability through the Last Will, publish groups. |
| `ZeroZero.Mqtt.Discovery` | `mqtt` | `net10.0` | The entity and discovery-document layer above it: seven typed component types, one device document, eviction that survives a process restart. |
| `ZeroZero.Mqtt.WinUI` | `mqtt` | `net10.0-windows10.0.26100.0` | The MQTT settings panel a host embeds. |
| `ZeroZero.Primitives` | `primitives` (foundation) | `net10.0` | The two-member log sink and its no-op, the reader of the version an assembly reports with its About-box form, the coalescing gate, and the source-revision stamp as build properties and targets. |
| `ZeroZero.SettingsShell.WinUI` | `settingsshell` | `net10.0-windows10.0.26100.0` | The settings window with every page left to the application: Mica chrome with the title bar painted for the theme, a navigation pane with a product footer, one scroll viewer over the pages, placement against the application's saved rectangle, Escape to close, and a section lifecycle with enter and leave hooks and a per-section build-once flag. Takes `ZeroZero.Controls.WinUI`. |
| `ZeroZero.Startup` | `startup` | `net10.0` | The application's logon task in the Task Scheduler: identity, the power-safe elevated definition, registration, the direct enabled read, enable, disable, delete, repair and demand-start verification. Windows only. |
| `ZeroZero.Tray` | `tray` | `net10.0` | The plain half of the tray component: the PNG-in-ICO file writer, the slot size at the taskbar's own scale, and whether the taskbar is light or dark with the stroke tone that reads on it. Headless, no drawing; takes `ZeroZero.Win32`. Windows only. |
| `ZeroZero.Tray.WinUI` | `tray` | `net10.0-windows10.0.26100.0` | The component's entry point: the tray icon host for a WinUI 3 application — the icon's lifecycle with the notify-icon library's efficiency mode refused, the theme, display and shell-restart listeners, the rendered-file cache, tooltip discipline, click classification and the menu refresh protocol. Drawing and notifications stay with the application. Carries `ZeroZero.Tray` with it; takes the notify-icon library and `Microsoft.Win32.SystemEvents`, and no type of either reaches its public signature. |
| `ZeroZero.Update` | `update` | `net10.0` | The update flow without its dialogs: the latest GitHub release against the running version, the download into a fresh private directory, verification of the installer — its Authenticode signature and publisher against the expected signer, and its SHA-256 against the hash the release publishes — before it runs, the launch-or-refuse policy, the stale-download sweep and the check scheduler. Takes `ZeroZero.Primitives`. Windows only. |
| `ZeroZero.Update.Win32` | `update` | `net10.0` | The component's entry point: the update task dialog and message boxes, worded here and marshalled by `ZeroZero.Win32`, and the check-ask-download-verify-launch orchestration that hands over to the application's own shutdown. Carries `ZeroZero.Update` with it. Windows only. |
| `ZeroZero.Win32` | `win32` (foundation) | `net10.0` | The raw Win32 layer: monitor, DPI and taskbar metrics as plain numbers, the native task dialog and message boxes, dark native chrome. No XAML, no Windows App SDK. |

Taking `ZeroZero.Mqtt.WinUI` brings the whole MQTT module and the primitives, config, controls and
win32 foundations with it, so an application with a settings page needs the one reference; the About
control is the brand component's and is a reference of its own. A headless or test consumer takes
`ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` and pulls in no WinUI at all — the primitives and
config foundations only.

**Versions are per component.** A package version is its component's tag without the prefix —
`mqtt-v0.7.0` is `0.7.0` — and a component's release notes speak about that component alone. A
package depends on the foundation packages it takes at the version declared when it was released.
Every package at `0.6.0` and below was released together under one number; from `0.7.0` each
component moves on its own.

## The feed authenticates every read

These packages are published to GitHub Packages at
`https://nuget.pkg.github.com/0z00z0/index.json` and nowhere else. **GitHub authenticates every
read, including of a public package** — an anonymous request returns 401 — so a restore needs a
token carrying `read:packages`, on a developer's machine and on a CI runner alike.

A consumer also needs package source mapping, so that no other feed can answer for a `ZeroZero.*`
name. The recipe, both halves of it, is in
[`docs/consuming.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/consuming.md).

## Requirements

.NET 10 SDK. The five WinUI packages additionally need Windows 10 1809 (build 10.0.17763) or later
and the Windows App SDK, which arrives as a package of its own. The MQTT module needs an MQTT 5.0
broker at run time, and Home Assistant 2024.11.0 or later for discovery.

## Documentation

- [README](https://github.com/0z00z0/0z0-shared#readme) — the component table, requirements, build
  and the documentation index.
- [`docs/consuming.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/consuming.md) — both
  reference routes, the CI shapes, pinning per component, the traps and the third-party pins.
- [`docs/zerozero-brand.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-brand.md) —
  the brand component: the About control and window, the two hosting styles.
- [`docs/zerozero-config.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-config.md) —
  the config foundation assembly.
- [`docs/zerozero-controls.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-controls.md) —
  the controls foundation assembly: the settings-row vocabulary, title-bar theming and the text
  prompt.
- [`docs/zerozero-diagnostics.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-diagnostics.md) —
  the diagnostics component: the crash handlers, the crash line, the version line, the dump
  registration and its lifecycle, and the wiring order.
- [`docs/zerozero-primitives.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-primitives.md) —
  the primitives foundation assembly: the log sink, the version reader, the coalescing gate and the
  source-revision stamp.
- [`docs/zerozero-settingsshell.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-settingsshell.md) —
  the settings shell component: the division between shell and pages, the section lifecycle,
  placement, theming and the traps.
- [`docs/zerozero-tray.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-tray.md) —
  the tray component: the host's lifecycle, listeners, cache, tooltip discipline, click
  classification and menu protocol, what stays with the application, and the plain half — the icon
  file writer, the slot size at the taskbar's scale, and the taskbar's theme.
- [`docs/zerozero-win32.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-win32.md) —
  the Win32 foundation assembly, and the manifest dependency its task dialog needs.
- [`docs/zerozero-lifecycle.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-lifecycle.md) —
  the lifecycle component: the lock, the relaunch and its limit, the data path, and the wiring order.
- [`docs/zerozero-startup.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-startup.md) —
  the startup component: the logon task, its definition and repair, and what stays with the application.
- [`docs/zerozero-update.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-update.md) —
  the update component: the two verification checks, where the published hash comes from, the
  wiring, and what stays with the application.
- [`docs/zerozero-build.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-build.md) —
  the build kit: the property blocks, the WinUI application block, the manifest template, signing,
  the pin rule and its guards, and the two ways to take it.
- [`docs/zerozero-mqtt.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-mqtt.md) —
  the MQTT module end to end: the six wiring steps, the entity model, identity, encryption, and the
  settings panel.
- [Release notes](https://github.com/0z00z0/0z0-shared/tree/main/docs/release-notes) — every
  consumer-visible change, per tag, under the component's folder. Every component is pre-1.0, so a
  minor bump may break.
