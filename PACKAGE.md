# ZeroZero Software — shared library

The building blocks ZeroZero Software's desktop apps share, published as packages from
[0z00z0/0z0-shared](https://github.com/0z00z0/0z0-shared) — one package per assembly, each
versioned and released with its component. MIT licensed, public.

| Package | Component | Target | What it is |
|---|---|---|---|
| `ZeroZero.Brand.Core` | `brand` | `net10.0` | Brand constants and the About-window data contracts. |
| `ZeroZero.Brand.WinUI` | `brand` | `net10.0-windows10.0.26100.0` | The About window, the hosted About control and the settings-row info icon. |
| `ZeroZero.Config` | `config` (foundation) | `net10.0` | Atomic JSON settings files: typed snapshot reads, mutation under one lock, change notification, quarantine of a file that cannot be parsed. |
| `ZeroZero.Mqtt` | `mqtt` | `net10.0` | An MQTT 5.0 connection for desktop applications: endpoint search and probe, retained per-entity channels, command routing, availability through the Last Will, publish groups. |
| `ZeroZero.Mqtt.Discovery` | `mqtt` | `net10.0` | The entity and discovery-document layer above it: seven typed component types, one device document, eviction that survives a process restart. |
| `ZeroZero.Mqtt.WinUI` | `mqtt` | `net10.0-windows10.0.26100.0` | The MQTT settings panel a host embeds. |
| `ZeroZero.Primitives` | `primitives` (foundation) | `net10.0` | The two-member log sink and its no-op, the reader of the version an assembly reports with its About-box form, the coalescing gate, and the source-revision stamp as build properties and targets. |
| `ZeroZero.Win32` | `win32` (foundation) | `net10.0` | The raw Win32 layer: monitor and DPI metrics as plain numbers, the native task dialog and message boxes, dark native chrome. No XAML, no Windows App SDK. |

Taking `ZeroZero.Mqtt.WinUI` brings the whole MQTT module, both brand assemblies and all three
foundation assemblies with it, so an application with a settings page needs the one reference. A
headless or test consumer takes `ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` and pulls in no WinUI
at all — the primitives and config foundations only.

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

.NET 10 SDK. The two WinUI packages additionally need Windows 10 1809 (build 10.0.17763) or later
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
- [`docs/zerozero-primitives.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-primitives.md) —
  the primitives foundation assembly: the log sink, the version reader, the coalescing gate and the
  source-revision stamp.
- [`docs/zerozero-win32.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-win32.md) —
  the Win32 foundation assembly, and the manifest dependency its task dialog needs.
- [`docs/zerozero-mqtt.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-mqtt.md) —
  the MQTT module end to end: the six wiring steps, the entity model, identity, encryption, and the
  settings panel.
- [Release notes](https://github.com/0z00z0/0z0-shared/tree/main/docs/release-notes) — every
  consumer-visible change, per tag, under the component's folder. Every component is pre-1.0, so a
  minor bump may break.
