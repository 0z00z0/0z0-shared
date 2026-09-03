# The config foundation assemblies

Settings on disk, in two assemblies. `ZeroZero.Config` is one JSON file holding one type: an atomic
write, typed snapshot reads, mutation under one lock, change notification and quarantine of a file
that cannot be parsed. `ZeroZero.Config.Sections` is one document whose top-level keys are sections
owned by different components, addressed one section at a time, plus the migration from an older
file to a new one. Both are plain `net10.0`, with no package references and no domain vocabulary —
which is what makes them **foundation** rather than a component: any component may take them, and
the MQTT module does, for its settings file and its discovery ledger.

Both are versioned as `ConfigVersion` in `Versions.props` and released under `config-v<x.y.z>` tags,
with notes under `docs/release-notes/config/`; [`releasing.md`](releasing.md) has the procedure. A
component that references either can only release once the version it references is on the feed, so
a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Any .NET 10 target; nothing Windows-specific. |

## Which one to take

| The file holds | Take |
|---|---|
| One type, and this application owns the whole file | `ZeroZero.Config`, and `SettingsFile<T>` |
| Several unrelated sections, or a section another component owns | `ZeroZero.Config.Sections` |

The sections assembly references the plain one, so taking it brings both.

## `ZeroZero.Config` — one file, one type

- **`SettingsFile<T>`** — a typed snapshot read, mutation under one lock, a write that lands whole
  or not at all, and a change event. The snapshot is what callers hold, so a reader never observes a
  half-applied edit.
- **`SettingsFileOptions`** — where the file lives, how it serialises, and what to do on failure.
- **`SettingsFileQuarantine`** — what happens to a file that cannot be parsed: it is copied aside,
  timestamped and marked `.bad`, *and* the original is overwritten with defaults immediately, as the
  store is constructed. The copy is the only surviving record and the three most recent are kept.
  Nothing surfaces it, so a host should: `SettingsFile<T>.LastQuarantinePath` names the copy, and a
  host that leaves it unread leaves its user without a configuration and with nothing on screen to
  say so.
- **`AtomicFile`** — the write itself, usable on its own: the content goes to a temporary sibling, is
  flushed through to the disk, and only then replaces the target, so neither a crash nor a power loss
  can leave half a file where a whole one was. A replace the operating system refuses for a moment —
  a scanner, an indexer, a closing handle — is retried five times, twenty milliseconds apart. It
  never throws; the exception that stopped the write is returned.
- **`SettingsSaveFailedEventArgs`** and **`SettingsSaveResult`** — a failed write is reported, never
  swallowed.

**A file that cannot be read is not written over.** A file that is present but unreadable when the
store is constructed — held open by another process, or access denied — reads as the declared
defaults in memory, and every `Update` and `Save` is refused until a `Reload` has read it once: the
result carries an `InvalidOperationException` and `SaveFailed` is raised. The file may be intact, and
quarantine cannot cover it, because the copy is taken by reading the file. A `Reload` that meets the
same failure keeps the state already held. Once any read has succeeded the store stays writable
whatever a later read finds: writing a good configuration over a file broken by hand is the intended
repair.

## `ZeroZero.Config.Sections` — one document, many owners

The document is one JSON object. Its first key is `version`, a whole number; every other key holds
an object, and each of those is a **section** belonging to whichever component asked for it. A store
addresses one section. It never addresses the document.

- **`SectionedSettingsFile`** — the document. `Section<T>(name)` hands out a store over one named
  section; several may address one document. `Reload()` re-reads it, `Keys` lists what it carries
  whether or not this build has a type for any of it, `DocumentVersion` and `IsFromNewerVersion`
  report the version, and `SaveFailed` reports a write that did not land.
- **`SettingsSection<T>`** — the store: `Read()`, `Update(Action<T>)`, `Write(T)` and a `Changed`
  event. `IsPresent` and `IsUnreadable` say whether the document carries the section and whether this
  build can read it.
- **`SectionedSettingsOptions`** — the directory and file name, the serialiser, the quarantine
  policy, the notification context, the document version this build writes, and the order sections
  take.
- **`SettingsMigration`** with `SettingsMigrationRequest`, `SettingsSectionMove`,
  `SettingsMigrationResult` and `SettingsMigrationOutcome` — below.

### What a write touches, and what it does not

A write replaces the byte ranges of the values it changes, and copies every other byte across
unchanged. That is the mechanism, not a rule the code remembers, so all of the following hold by
construction:

- **A sibling section is untouched.** It is never read, never bound to any type, never rewritten.
- **A section this build has no type for survives**, including one written by a version that no
  longer exists.
- **One value this build cannot read costs its own section and nothing else.** The document walk
  reads structure only; a value of the wrong kind, or an enum member no type answers to, is walked
  past without ever being bound. The section holding it reads as its type's defaults, the document is
  copied aside, and its bytes stay in the file.
- **The file's key order is the file's own.** No type declares it, and nothing rebuilds the document
  from a type. The declared order is consulted in exactly one place: choosing where a section the
  document does not yet carry is inserted.
- **Hand edits survive.** Comments, trailing commas, the file's own indent, its line ending and its
  byte-order mark are read off the file and kept. An unknown member inside a section this build does
  own survives too.
- **A member is matched the way the reader matches it.** With a case-insensitive serialiser, a write
  finds the file's own spelling and replaces its value rather than adding the declared spelling
  beside it — two keys differing only in case leave the last one governing the read and the first one
  dead.

What a write does change: the values of the members the section's type declares, whenever they differ
from what the file says. A number the type holds as `0.75` is written as `0.75` even where the file
said `0.750`. Nothing outside the section moves.

### The version key

- Written as the first key **only when the document carries none**. An existing version is never
  raised: sections belong to independently released components, so declaring that the whole document
  has moved to a new shape is a decision above any one section, and it belongs to the migration.
- A document declaring a version **above** the one this build writes is neither read nor written.
  Every section reads as its defaults, every write is refused, and `IsFromNewerVersion` says so. A
  newer peer owns keys this build would not understand, and defaults written over them would be
  exactly the loss the design exists to prevent. The check runs again inside every write, so a
  document that becomes newer between the read and the write is still not written over.
- A document with **no** version key is the older, flat shape and is read as it stands.

### The write latch, and what is preserved

Writing is refused until a read has succeeded, and that latch is set once and never cleared. A file
held open by another process may be perfectly intact, so nothing is written over it; once anything
has been read, writing a good configuration over a file broken by hand is the intended repair and
stays allowed. A missing file and an empty file both count as a read: there is nothing to lose.

Every write consults the document on disk rather than what memory holds, so an edit made out of band
since the last read is part of the file the write preserves.

A document this build cannot read is copied aside **from the bytes already in hand**, never by
reading the file a second time — the copy has to work for the file whose second read would fail too.
A file that could not be read at all therefore gets no copy, and is not written over either.
`LastQuarantinePath` names the copy; the three most recent are kept.

### Taking a section

```csharp
var document = new SectionedSettingsFile(new SectionedSettingsOptions(directory, "settings.json")
{
    Version = 1,
    SectionOrder = ["general", "graph", "mqtt", "window"],
});

var general = document.Section<GeneralSettings>("general");
var settings = general.Read();
var result = general.Update(s => s.StartMinimised = true);
```

The section type is a class with a parameterless constructor that serialises to a JSON object; a type
that does not is refused when the section is taken, not on the first save a person makes. A component
declaring its storage dependency as three members — a read, an update and a change event — is
satisfied by `SettingsSection<T>` through a few lines of adapter in the application;
`IMqttSettingsStore` in the MQTT module is that shape.

## The migration

`SettingsMigration.Run` writes a new settings file from an old one and **leaves the old one
completely alone**. The old file is opened to read and nothing else: never written, never renamed,
never deleted, not even on success. Retiring it is the application's decision, taken once it has seen
its own load from the new file succeed — so a migration that goes wrong costs nothing, because the
file it came from is still exactly where it was.

```csharp
var result = SettingsMigration.Run(new SettingsMigrationRequest(oldPath, newPath)
{
    Version = 1,
    Moves =
    [
        new SettingsSectionMove("general", ["startMinimised", "pollSeconds"]),
        new SettingsSectionMove("graph", ["graphSpan", "thresholdWarn"]),
    ],
});
```

- Every top-level key of the old file lands in the new one: inside the section it was mapped into, or
  at the top level where it already was. A key no move names is carried through, which is how a
  section this build has no type for survives the move.
- **A migration groups keys; it never renames them.** The member name inside the new section is the
  old key's own name, carried as the file's own bytes, so a key holding an escape sequence is written
  back exactly as it was. The section type has to bind to that name — casing aside, which the
  serialiser handles.
- Values are carried as the bytes the old file held, never re-serialised through any type.
- Comments are carried with the key they sit above; a comment written inside a value travels inside
  that value.
- The old file's byte-order mark, line ending and indent are the new file's.
- **Before it reports success the new file is read back off the disk** and checked against the old
  one, walked again from scratch: every key present, every value the same value, every comment
  arrived, the version as requested. If any of that fails the new file is removed and the outcome is
  `NotProven` with `Missing` naming what did not arrive.

`SettingsMigrationOutcome` carries the rest: `TargetAlreadyExists` (nothing read, nothing written),
`SourceMissing`, `SourceUnreadable`, `SourceNotADocument`, `WriteFailed`, and `RequestRefused` for a
request that contradicts itself — the same path twice, one key claimed by two sections, or a section
whose name the old file already uses as a top-level key.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Config.Sections` for a
sectioned document, or `ZeroZero.Config` alone for a file holding one type; there is nothing beneath
them. An application taking a component that already references the plain assembly — the MQTT module
does — has it transitively and adds nothing.

The tests are in `tests/ZeroZero.Config.Tests` and `tests/ZeroZero.Config.Sections.Tests`, plain
`net10.0`, and run on any machine with the SDK: no desktop, no broker, no network. The migration's
own proof runs against `tests/ZeroZero.Config.Sections.Tests/Fixtures/awkward-settings.json`, a
settings file carrying every awkward property an installed one has.
