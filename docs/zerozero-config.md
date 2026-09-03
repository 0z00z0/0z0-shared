# The config foundation assemblies

Settings on disk, in three assemblies. `ZeroZero.Config` is one JSON file holding one type: an atomic
write, typed snapshot reads, mutation under one lock, change notification and quarantine of a file
that cannot be parsed. `ZeroZero.Config.Sections` is one document whose top-level keys are sections
owned by different components, addressed one section at a time, plus the migration from an older
file to a new one. `ZeroZero.Config.Watch` picks up an edit made outside the application without a
restart: one file watcher, a quiet window so a single save arrives once, and a classifier that says
whether what changed was worth reacting to. All three are plain `net10.0`, with no package references
and no domain vocabulary — which is what makes them **foundation** rather than a component: any
component may take them, and the MQTT module does, for its settings file and its discovery ledger.

All three are versioned as `ConfigVersion` in `Versions.props` and released under `config-v<x.y.z>` tags,
with notes under `docs/release-notes/config/`; [`releasing.md`](releasing.md) has the procedure. A
component that references any of the three can only release once the version it references is on the
feed, so a change here releases first.

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

The sections assembly references the plain one, so taking it brings both. `ZeroZero.Config.Watch` is
separate from that choice: it watches either, and is taken only by an application that wants a hand
edit to take effect without a restart.

## `ZeroZero.Config` — one file, one type

- **`SettingsFile<T>`** — a typed snapshot read, mutation under one lock, a write that lands whole
  or not at all, and a change event. The snapshot is what callers hold, so a reader never observes a
  half-applied edit.
- **`SettingsFileOptions`** — where the file lives, how it serialises, and what to do on failure.
- **`SettingsFileQuarantine`** — what happens to a file that cannot be parsed: it is copied aside,
  timestamped and marked `.bad`, *and* the original is overwritten with defaults immediately. That
  happens on every read of the file, so a `Reload` that finds it broken quarantines it exactly as
  construction does. The copy is the only surviving record and the three most recent are kept.
  Nothing surfaces it, so a host should: `SettingsFile<T>.LastQuarantinePath` names the copy, and a
  host that leaves it unread leaves its user without a configuration and with nothing on screen to
  say so.
- **`AtomicFile`** — the write itself, usable on its own: the content goes to a temporary sibling, is
  flushed through to the disk, and only then replaces the target, so neither a crash nor a power loss
  can leave half a file where a whole one was. A replace the operating system refuses for a moment —
  a scanner, an indexer, a closing handle — is attempted five times in all, twenty milliseconds
  apart: one try and four retries. It never throws; the exception that stopped the write is returned.

**A whole-file store is not a document preserver.** Everything the type does not declare is gone at
its next write: comments, the file's own key order, and any member no property answers to. Reading
deserialises the file into the type and holds the result, and every write serialises that type out
again, so the file after a save is the type's shape and nothing else. That is the trade for a store
this simple; where a file has to survive being edited by hand, or is shared with a component this
build has no type for, the sectioned store below is the one that preserves it.
- **`SettingsSaveFailedEventArgs`** and **`SettingsSaveResult`** — a failed write is reported, never
  swallowed.

**A file that cannot be read is not written over.** A file that is present but unreadable when the
store is constructed — held open by another process, or access denied — reads as the declared
defaults in memory, and every `Update` and `Save` is refused until a `Reload` has read it once: the
result carries an `InvalidOperationException` and `SaveFailed` is raised. The file may be intact, and
quarantine cannot cover it, because the copy is taken by reading the file. A `Reload` that meets the
same failure keeps the state already held. Once any read has succeeded the store stays writable
whatever a later read finds: writing a good configuration over a file broken by hand is the intended
repair. `SettingsFile<T>` reports the latch through nothing: a host learns of it from the refused
save, which is why the result is worth reading. The sectioned store carries it as
`SectionedSettingsFile.HasLoaded`.

## `ZeroZero.Config.Sections` — one document, many owners

The document is one JSON object. A key holding an object is a **section**, belonging to whichever
component asked for it, and `version` is the one key that is not: a whole number saying which shape
of the document this is. A store addresses one section. It never addresses the document.

Neither of those is enforced, and a guide that said they were would be describing a document the
store rejects and it does not. `version` is written first when the document carries none, and stays
wherever the file already has it otherwise — key order is the file's own, as
[below](#what-a-write-touches-and-what-it-does-not). A document may carry no version key at all,
which is the older flat shape, and `DocumentVersion` reads null for it. A top-level key holding
something other than an object is carried across like any other byte until a store addresses that
name, at which point the store replaces what stands in its place, because that name is its section.

**One document is not all of an application's configuration.** A component may own a file of its own
instead of a section, and a component with a section here may still keep the bulk of its
configuration elsewhere — the MQTT module keeps its broker settings and its discovery ledger in two
files of its own through `SettingsFile<T>`, and an application's settings document may hold an `mqtt`
section that is nothing but a last-good endpoint memory. Nothing here assumes otherwise:
`SectionedSettingsFile` is one file, several may exist side by side, and `SettingsSection<T>` states
that what sits behind it is not its caller's business. **A migration moves keys within one document**;
a key belonging in another component's own file is carried where it stands, not relocated.

- **`SectionedSettingsFile`** — the document. `Section<T>(name)` hands out a store over one named
  section; several may address one document. `Reload()` re-reads it, `Keys` lists what it carries
  whether or not this build has a type for any of it, `DocumentVersion` and `IsFromNewerVersion`
  report the version, `HasLoaded` says whether a read has ever succeeded — false is the latch below,
  with every write refused — and `SaveFailed` reports a write that did not land.
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
  own survives too, and so does a comment that is the only thing inside a section.
- **A member is matched the way the reader matches it.** With a case-insensitive serialiser, a write
  finds the file's own spelling and replaces its value rather than adding the declared spelling
  beside it.

What a write does change: the values of the members the section's type declares, whenever they differ
from what the file says. A number the type holds as `0.75` is written as `0.75` even where the file
said `0.750`. Nothing outside the section moves.

### Comments: tolerated on read, never written

**A comment in the file costs nothing to read, and the store never writes one.** The asymmetry is
deliberate and it is a property of the store rather than advice. A reader that leaves comment
handling at disallow — which is what a consuming application uses — does not degrade on a comment, it
fails the whole file, and the person sees a settings file that has stopped working. So no path
through a write composes one: the bytes are the serialiser's output plus punctuation and indent, and
a value or a key that happens to spell `//` is written quoted like any other text.

A comment the file already carried and the write did not touch stays where it is. That is
preservation — its bytes were copied across, not authored — and it is why a document that arrives
with comments still has them afterwards. A consumer whose own reader disallows comments should treat
such a document as one to repair, not one the store has damaged.

### Two keys that differ only in case

A reader takes the last of two keys of the same name, so a build that writes its own spelling beside
the file's leaves the person's value in the file with nothing reading it. **Every write that would
create such a pair is refused**, with `SettingsKeyCaseConflictException` naming both spellings and the
file left exactly as it was. `SaveFailed` announces it like any other refused write.

**The three places it can happen do not behave alike, and the difference is the whole of it.** A
section key and the version key are matched letter for letter, always: the document walk compares
them ordinally, whatever the serialiser is set to, so `Mqtt` never finds `mqtt` and the write that
would add the second one is refused. A member inside a section is matched the way the serialiser
matches it — and the family's serialiser is case-insensitive, so the write finds the file's own
spelling and replaces the value behind it. **Under the default serialiser a member can never reach
the refusal at all.** It is reachable only where a consumer supplies a case-sensitive serialiser, and
then it behaves like the other two.

So the case a consumer will actually meet is a section key, and it is the one worth planning for:
address a section by the spelling the installed file already uses.

On the read side, `SettingsSection<T>.ConflictingKey` names the spelling the file holds when it
differs from the section's own name only in case. Without it a host would show an empty page and have
nothing to say about why.

**The limit is worth stating: this catches a difference of case, not a difference of word.**
`GraphLineColouring` and `GraphLineColoring` are two spellings of the same idea and nothing can tell
that one was meant for the other, so a type declaring the second reads nothing from a file holding the
first and a write adds it alongside. Match the file's own spelling, letter for letter.

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

**What it is for, stated narrowly.** Grouping a flat file into sections is for an installation older
than the application's own move to sections, and for an application whose file is still flat. It is
not what a current installation of either application necessarily needs: one of them was measured,
and its installed file is already section-addressed and version-stamped, so the flat shape the
grouping exists for is one that installation has already left behind. For a document that is already
sectioned the migration does something narrower and still worth having — it carries every key into a
new file and stamps the version this build asks for, which is the one thing a section store
deliberately refuses to do, and it does it without touching the file it read. Pass no moves for that.

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
  back exactly as it was. The section type has to bind to that name — casing aside, and only where the
  serialiser is the case-insensitive one.
- Values are carried as the bytes the old file held, never re-serialised through any type.
- **No comment is written.** One outside a value is named in `CommentsNotCarried` and left behind in
  the old file, which is never touched; one inside a value travels with that value's bytes and is
  named in `CommentsInsideValues`, so an application whose reader disallows comments knows before it
  reads. The reason is the store's own rule: a comment does not degrade such a reader, it fails the
  file.
- The old file's byte-order mark, line ending and indent are the new file's.
- **Before it reports success the new file is read back off the disk** and checked against the old
  one, walked again from scratch: every key present, every value the same value, the version as
  requested, and exactly the comments a carried value brought with it and no others. If any of that
  fails the new file is removed and the outcome is `NotProven` with `Missing` naming what did not
  arrive.

`SettingsMigrationOutcome` carries the rest: `TargetAlreadyExists` (nothing read, nothing written),
`SourceMissing`, `SourceUnreadable`, `SourceNotADocument`, `WriteFailed`, and `RequestRefused` for a
request that contradicts itself — the same path twice, one key claimed by two sections, a section
whose name the old file already uses as a top-level key, or a move whose section or key differs from
one the old file carries only in case. A move naming a key the old file does not carry at all is
ordinary and is simply absent from the result.

## `ZeroZero.Config.Watch` — an edit that takes effect without a restart

A settings file that can be opened in a text editor is only half a promise if the application has to
be restarted before it notices. `SettingsWatcher<T>` closes that: it watches the file, waits for the
save to finish, has the store read the file again, and raises `Changed` when what moved was worth
moving.

```csharp
var classifier = new SettingsChangeClassifier<AppSettings>(
    "must the connection be rebuilt?",
    ["WindowWidth", "WindowHeight", "Window/Left", "Window/Top"]);

var watcher = SettingsWatcher<AppSettings>.For(store, classifier);
watcher.Changed += (_, e) => Reconnect(e.After);
```

`For` wires a `SettingsFile<T>`. Anything else — a single section of a sectioned document, or a store
of the application's own — is wired through `SettingsWatcherOptions<T>`, which asks for the path, a
way to read what the store holds, a way to tell it to read the file again, and the classifier. A
fifth, `Obstruction`, is optional and is [below](#when-the-store-can-read-the-file-and-still-see-nothing-in-it).
The watcher never parses the file, so it needs no knowledge of the document's shape.

### The classifier, and which way its default falls

The classifier serialises both states, removes the values a list names, and compares what is left.
**Everything the list does not name counts.** A property added a year later is weighed from the
moment it exists, and a value is only ever skipped because somebody wrote its name down. The
opposite arrangement — a list of what to watch — makes every new property invisible until somebody
remembers to add it, which is a reload that quietly stops firing and a defect nobody sees.

- A name on its own is a property of the settings shape; `window/left` names one value inside a
  nested object, and `window` on its own skips the whole object. Matching ignores case.
- A name whose first segment matches nothing is refused when the classifier is built, because a skip
  that has quietly stopped applying is invisible until somebody wonders why the application reacts
  to a window being moved.
- **The mechanism is general; the question is not.** The classifier carries the question it answers,
  because "did anything change?" and "must the connection be rebuilt?" have different answers over
  the same file, and one file may be watched by two classifiers asking different things.

### The application's own writes

A store writes its file constantly, and a watcher that reports those writes turns one save into an
endless loop. This one reports nothing, and does it without pausing around writes or remembering
what it last wrote: an examination compares **what the store held before re-reading against what it
holds after**. A write the store made leaves the file agreeing with what the store already holds, so
the re-read moves nothing and the two states are equal. An edit made by hand leaves them different.
Nothing here depends on a notification arriving inside a particular window, so a busy machine cannot
get it wrong.

### The quiet window

One save does not arrive as one notification. Measured on Windows, an in-place save arrives as two
change notifications, and the atomic write this library performs arrives as a delete followed by a
rename **with no change notification on the target at all** — so a watcher taking only the change
notification sleeps through every write the library makes, and one taking only the delete sleeps
through the first save an application ever makes, which arrives as a rename alone.

Every notification restarts a quiet window, 500 ms by default, and the file is examined once it
closes. The window is measured on a `TimeProvider`, so a test crosses it by moving a clock rather
than by sleeping for it.

### What else it reports

- `Examined` is raised after every examination, `Changed` only after one the classifier called
  substantive. A consumer can therefore tell a file that was looked at and dismissed from a file that
  was never looked at.
- `Failed` carries anything that went wrong on the watcher's own thread — a store that threw, or the
  operating system dropping notifications faster than they could be taken. Nothing is allowed to
  escape: the examination runs on a timer thread, where a throw bypasses the application's
  unhandled-exception handler and takes the process with it. After a dropped-notification failure
  the watcher rebuilds itself and forces an examination, so a missed edit costs a moment rather than
  a reload that never happens.
- `NotificationContext` posts all three — `Examined`, `Changed` and `Failed` — to a captured context,
  for a consumer that touches a user interface. All three, because an event whose thread depends on
  why it fired is one nothing correct can be written against: a consumer that has given a context
  never has to ask which thread it is on.

### When the store can read the file and still see nothing in it

A section store has a state a whole-file store does not: the document spells the section's name in a
case the addressing type does not, so the section reads as its type's defaults for ever and every
write to it is refused. **Measured on the built code**: the re-read does not throw and does not
fail — it succeeds and moves nothing, so both states either side of it are equal and the examination
is honestly not a change, however the file is edited. Left there, a person correcting a value by hand
watches nothing happen and is told nothing.

`SettingsWatcherOptions<T>.Obstruction` closes that. It is the store's own sentence saying what is in
the way, or null when nothing is, asked once per examination after the re-read. What it returns is
carried on every `SettingsChangeEventArgs<T>.Obstruction`, and announced through `Failed` as a
`SettingsWatchObstructedException` when it first appears and again if it changes — once, not on every
examination, because an obstruction stands until the file is repaired. Clearing it is not announced:
the edit that clears it reports itself as a change.

For a section the reason comes from `SettingsSection<T>.ConflictingKey`, which names the spelling the
document holds. A whole-file store names none, leaves the delegate unset, and nothing about it
changes.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Config.Sections` for a
sectioned document, or `ZeroZero.Config` alone for a file holding one type; there is nothing beneath
them. An application that also wants hand edits picked up adds `ZeroZero.Config.Watch`, which sits
beside them and references the plain assembly. An application taking a component that already
references the plain assembly — the MQTT module does — has it transitively and adds nothing.

The tests are in `tests/ZeroZero.Config.Tests`, `tests/ZeroZero.Config.Sections.Tests` and
`tests/ZeroZero.Config.Watch.Tests`, plain `net10.0`, and run on any machine with the SDK: no
desktop, no broker, no network — with one exception: the watcher's tests change real files on a real
disk and read what the watcher reports, only the clock being substituted, and two of them assert how
many notifications one save delivers and in what shape. That is a property of the Windows
directory-change API rather than of the assembly, so `tests/ZeroZero.Config.Watch.Tests` runs on
Windows. The assembly itself is plain `net10.0` and carries nothing Windows-specific.

The migration is proven against two fixtures, and the difference between them is the point:

- `Fixtures/installed-settings.json` is the shape an installed file has — already sectioned and
  version-stamped, lower-case section keys with underscores, upper camel case members, no byte-order
  mark, no comments, no trailing commas. It carries the three member spellings that defeat a binder,
  and the proof is that the migration leaves every one of them alone. The shape was measured by a
  consuming application's own session and reported; no installed file is read here, and the member
  names other than those three are the fixture's own — except the `mqtt` section, which holds a
  last-good endpoint memory and therefore carries the members `MqttEndpointMemory` declares, since
  that type belongs to this repository.
- `Fixtures/awkward-settings.json` is a hand-edited worst case, not an installed shape: a byte-order
  mark, comments, indentation that changes halfway down, a duplicated key, a section from a build
  that no longer exists, a trailing comma. **A real file does not look like this**, and for a reader
  that disallows comments a file that does is one it cannot open at all. It is kept because reading
  such a file without losing anything is what the design claims and has to be shown.
