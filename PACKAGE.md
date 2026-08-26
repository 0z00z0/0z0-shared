# ZeroZero Software — shared library

The building blocks ZeroZero Software's desktop apps share, published as six packages from
[0z00z0/0z0-shared](https://github.com/0z00z0/0z0-shared). MIT licensed, public.

| Package | Target | What it is |
|---|---|---|
| `ZeroZero.Brand.Core` | `net10.0` | Brand constants and the About-window data contracts. |
| `ZeroZero.Brand.WinUI` | `net10.0-windows10.0.26100.0` | The About window, the hosted About control and the settings-row info icon. |
| `ZeroZero.Config` | `net10.0` | Atomic JSON settings files: typed snapshot reads, mutation under one lock, change notification, quarantine of an unreadable file. |
| `ZeroZero.Mqtt` | `net10.0` | An MQTT 5.0 connection for desktop applications: endpoint search and probe, retained per-entity channels, command routing, availability through the Last Will, publish groups. |
| `ZeroZero.Mqtt.Discovery` | `net10.0` | The entity and discovery-document layer above it: seven typed component types, one device document, eviction that survives a process restart. |
| `ZeroZero.Mqtt.WinUI` | `net10.0-windows10.0.26100.0` | The MQTT settings panel a host embeds. |

Taking `ZeroZero.Mqtt.WinUI` brings the whole MQTT module and both brand assemblies with it, so an
application with a settings page needs the one reference. A headless or test consumer takes
`ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` and pulls in no WinUI at all.

## The feed authenticates every read

These packages are published to GitHub Packages at
`https://nuget.pkg.github.com/0z00z0/index.json` and nowhere else. **GitHub authenticates every
read, including of a public package** — an anonymous request returns 401 — so a restore needs a
token carrying `read:packages`, on a developer's machine and on a CI runner alike.

A consumer also needs package source mapping, so that no other feed can answer for a `ZeroZero.*`
name. The recipe, both halves of it, is in
[the README](https://github.com/0z00z0/0z0-shared#1-reference-the-library).

## Requirements

.NET 10 SDK. The two WinUI packages additionally need Windows 10 1809 (build 10.0.17763) or later
and the Windows App SDK, which arrives as a package of its own. The MQTT module needs an MQTT 5.0
broker at run time, and Home Assistant 2024.11.0 or later for discovery.

## Documentation

- [README](https://github.com/0z00z0/0z0-shared#readme) — the projects, the About dialogue, the
  screenshots, the version pins.
- [`docs/zerozero-mqtt.md`](https://github.com/0z00z0/0z0-shared/blob/main/docs/zerozero-mqtt.md) —
  the MQTT module end to end: the six wiring steps, the entity model, identity, encryption, and the
  settings panel.
- [Release notes](https://github.com/0z00z0/0z0-shared/tree/main/docs/release-notes) — every
  consumer-visible change, per tag. The module is pre-1.0, so a minor bump may break.
