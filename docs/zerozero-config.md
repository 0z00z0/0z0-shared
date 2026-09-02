# The config foundation assembly

`ZeroZero.Config` is the settings store: an atomic JSON file with typed snapshot reads, mutation
under one lock, change notification and quarantine of a file that cannot be parsed. Plain
`net10.0`, no package references, no project references, and no domain vocabulary — which is what
makes it **foundation** rather than a component: any component may take it, and the MQTT module
does, for its settings file and its discovery ledger. It is equally usable on its own by anything
that keeps a JSON document on disk.

The assembly is versioned as `ConfigVersion` in `Versions.props` and released under
`config-v<x.y.z>` tags, with notes under `docs/release-notes/config/`; [`releasing.md`](releasing.md)
has the procedure. A component that references it can only release once the version it references
is on the feed, so a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Any .NET 10 target; nothing Windows-specific. |

## What it contains

- **`SettingsFile<T>`** — an atomic JSON file: a typed snapshot read, mutation under one lock, a
  write that lands whole or not at all, and a change event. The snapshot is what callers hold, so a
  reader never observes a half-applied edit.
- **`SettingsFileOptions`** — where the file lives, how it serialises, and what to do on failure.
- **`SettingsFileQuarantine`** — what happens to a file that cannot be parsed: it is copied aside,
  timestamped and marked `.bad`, *and* the original is overwritten with defaults immediately, as the
  store is constructed. The copy is the only surviving record and the three most recent are kept.
  Nothing surfaces it, so a host should: `SettingsFile<T>.LastQuarantinePath` names the copy, and a
  host that leaves it unread leaves its user without a configuration and with nothing on screen to
  say so.
- **`SettingsSaveFailedEventArgs`** and **`SettingsSaveResult`** — a failed write is reported, never
  swallowed.

**A file that cannot be read is not written over.** A file that is present but unreadable when the
store is constructed — held open by another process, or access denied — reads as the declared
defaults in memory, and every `Update` and `Save` is refused until a `Reload` has read it once: the
result carries an `InvalidOperationException` and `SaveFailed` is raised. The file may be intact,
and quarantine cannot cover it, because the copy is taken by reading the file. A `Reload` that meets
the same failure keeps the state already held. Once any read has succeeded the store stays writable
whatever a later read finds: writing a good configuration over a file broken by hand is the intended
repair.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Config` itself; there is
nothing beneath it. An application taking a component that already references it — the MQTT module
does — has it transitively and adds nothing.

The tests are in `tests/ZeroZero.Config.Tests`, plain `net10.0`, and run on any machine with the
SDK: no desktop, no broker, no network.
