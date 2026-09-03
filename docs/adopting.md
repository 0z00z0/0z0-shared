# Adopting the shared library

The document an application works from when it takes these components. Each component has a guide of
its own and this one repeats none of them: it says what exists, which one thing to reference for
each, in what order to take them, what the application has to supply, and the traps that appear only
while adopting.

Read [`consuming.md`](consuming.md) once first. It carries the two reference routes, the CI shapes
and how a pin works, and everything below assumes it.

## What exists, and the one thing to reference

| Component | What the application gets | Reference | Guide |
|---|---|---|---|
| Brand | The studio mark, the About box, the typeface, and the palette as brushes XAML can merge | `ZeroZero.Brand.WinUI`; a console tool takes `ZeroZero.Brand.Core` alone | [brand](zerozero-brand.md) |
| Diagnostics | One place every unhandled exception lands, a crash line that never throws, the version line at startup, and crash dumps | `ZeroZero.Diagnostics`; the dump registration alone is `ZeroZero.Diagnostics.Dumps` | [diagnostics](zerozero-diagnostics.md) |
| Lifecycle | One instance at a time, relaunch under a limit after a clean exit nobody asked for, and the per-user data folder | `ZeroZero.Lifecycle` | [lifecycle](zerozero-lifecycle.md) |
| MQTT | The application on a broker and into Home Assistant as one device with entities, and the settings panel | `ZeroZero.Mqtt.WinUI`; headless, `ZeroZero.Mqtt` or `ZeroZero.Mqtt.Discovery` | [mqtt](zerozero-mqtt.md) |
| Settings shell | The settings window, with every page left to the application | `ZeroZero.SettingsShell.WinUI` | [settings shell](zerozero-settingsshell.md) |
| Startup | The run-at-logon task, and the repair of one an older build left | `ZeroZero.Startup` | [startup](zerozero-startup.md) |
| Tray | The tray icon's whole lifecycle — theme, display changes, shell restarts, tooltip, clicks, menu | `ZeroZero.Tray.WinUI`; the icon-file writer alone is `ZeroZero.Tray` | [tray](zerozero-tray.md) |
| Update | Check, download, verify, launch, hand over | `ZeroZero.Update.Win32`; headless, `ZeroZero.Update` | [update](zerozero-update.md) |
| Config | Settings on disk: one file holding one type, or one document divided into sections owned by different components | `ZeroZero.Config.Sections`, or `ZeroZero.Config` for a file of one type | [config](zerozero-config.md) |
| Controls | Settings rows with their info bubbles, title-bar theming, the text prompt — no studio identity | `ZeroZero.Controls.WinUI` | [controls](zerozero-controls.md) |
| Primitives | The log sink, the version reader, the coalescing gate, the commit stamp | `ZeroZero.Primitives` | [primitives](zerozero-primitives.md) |
| Win32 | Monitor, DPI and taskbar numbers, the native task dialog and message boxes, dark native chrome | `ZeroZero.Win32` | [win32](zerozero-win32.md) |
| Build kit | The shared build rules, the WinUI application block, the manifest, signing, and every third-party version | Not a reference at all — imports, below | [build](zerozero-build.md) |

Config, Controls, Primitives and Win32 are foundation: anything may take them, and most arrive on
their own — the MQTT reference alone brings all four. Take a foundation reference directly only where
nothing else brought it, or where a rule below says to.

**One reference per component, and one route for the whole repository.** Packages or a sibling
checkout, never a mixture: a mixture resolves a foundation assembly twice, once as a package and once
as the sibling's project, and two assemblies of one identity from two sources is a conflict the build
may only warn about.

**Three things an entry point does not bring, by design.**

- **The file watcher**, `ZeroZero.Config.Watch`. Noticing an edit made outside the application is a
  choice rather than a consequence of storing settings, so a consumer that wants it adds a second
  reference of the same component. It releases under the same tag and costs no extra pin.
- **The commit stamp**, which makes the version an assembly reports name the source it came from. It
  travels with a direct reference to `ZeroZero.Primitives` and with nothing else, so a component
  never restamps the application above it unasked. On the sibling route it is an import by path; the
  primitives guide has the line.
- **The settings rows and the info bubble.** The brand component does not carry
  `ZeroZero.Controls.WinUI`. An application that wants the About box and the rows takes both.

## In what order

Not the dependency order — dependencies arrive on their own. This is the order of what each one
costs to take.

1. **The build kit first.** It decides every third-party version and writes the manifest that the
   task dialog and per-monitor DPI depend on. Taken after the components, its pins arrive as
   conflicts rather than as rules.
2. **Then what nothing can see:** the version reader and the commit stamp, the Win32 numbers, the
   settings store. Each replaces a private copy with no visible change, so a mistake surfaces at
   build time rather than in front of a user.
3. **Then the process shape:** crash diagnostics, the single-instance lock, the logon task, the tray
   icon. These change where the application starts and stops. Take one at a time and run it between
   each.
4. **Then the surfaces:** the settings rows, the palette and About box, the settings window, the MQTT
   panel. Every one of them is judged by looking rather than by testing, so leave time to look.
5. **The update flow last.** It is the only component that cannot be proved without a real signed
   release fetched over the network, and its failure mode is an application that will not update.

## What the application supplies

| Component | What it is handed | What it never decides |
|---|---|---|
| Anything with a log sink | An object with two members, or nothing — every component falls back to the shared no-op | The logging framework. There is no seam for one |
| Config | The folder, the file name, and one class per section | The write, the lock, the quarantine |
| Diagnostics | The folder for the crash line, its own error mode, and the dump policy | Where Windows Error Reporting reads its settings |
| Lifecycle | The lock's name, the product folder's name, and the decision to exit deliberately | The relaunch limit, which is fixed |
| Startup | The task name, the executable and its arguments, and an elevated process to register from | The settings that keep the task running on battery and past the scheduler's own time limit |
| Tray | The drawing, the tooltip text, the menu, and the click actions | The icon file, the slot size, the taskbar's theme |
| Brand | Name, version, description, repository link, third-party credits | The studio mark, company name, tagline, website and donate links |
| Settings shell | The ordered sections, the saved-rectangle store, the product name, mark and version, the theme, and the few measurements the two applications choose differently | The window, the pane, the scroll viewer, the placement arithmetic |
| MQTT | The topic root, the device and origin blocks, the entity table, the publish groups, the settings store, the ledger, and the panel's application-shaped copy | Anything about the protocol, the discovery document, or the panel's own vocabulary |
| Update | The repository, the expected signer and its fingerprint, the installer's launch, and when the application exits | The verification, which is behaviour rather than an option |

## Traps

### The build kit is imports, not a reference

Referenced as an ordinary package it restores cleanly, delivers nothing, and then fails the build
with `ZZB011` naming the route that works — so the mistake is loud, but only after a restore that
looked fine.

What works is three imports at the repository's root: the kit's props from `Directory.Build.props`,
its targets from `Directory.Build.targets`, and its pins from `Directory.Packages.props`. **A WinUI
application project makes a fourth import of its own**, the application block, which is where the
output type, the target framework, the runtime identifier, the language default and the generated
manifest come from. The project-level `Sdk` attribute is not a fourth route, and it does not
fail quietly either: `Sdk="Microsoft.NET.Sdk;ZeroZero.Build"` restores cleanly and then stops the
build at evaluation with `MSB4019`, the imported project `Sdk\Sdk.props` not found. That attribute
imports `Sdk.props` and `Sdk.targets` by those names and the package carries neither: its `Sdk\`
folder holds the three files above and the WinUI block under their own names. The build guide
carries all four lines for both routes.

The kit pins the Windows App SDK and its build tools but references neither, so **a WinUI
application still declares both package references itself** — without a version, as below.

### Package versions live in the kit and nowhere else

A version attribute on a reference, a second declaration of a package the kit already pins, an update
moving a pin, and an override each fail the build — some stopped by the kit's own guard and the rest
by the restore, which the kit configures to refuse them. The version in the pin file is the version
the whole family resolves, and the only way to move one is to move it there.

The one gap worth knowing: restating a pin at the version it already carries passes silently. It buys
nothing and it will drift, so do not write one. Which pins are a floor and which a ceiling is in
[`consuming.md`](consuming.md#third-party-pins).

### The settings store is silent about a name it does not recognise

This is the sharpest edge in the set.

A section is found by its exact spelling, case included, and one the file spells in another case
reads as **the type's declared defaults** — no exception, no event, nothing in a log, and nothing to
tell it apart from a section that has never been written. A write then refuses to create the twin,
so the file cannot end up holding both — and the same refusal guards the document's `version` key.
Inside a section it is the serialiser that matches member names, and the family's default one ignores
case, so a member's case is harmless under it; an application that supplies a case-sensitive
serialiser gets the refusal there too.

That guard catches a difference of **case**. Nothing catches a difference of **word**, and an en-US
spelling where the file holds the en-GB one is a different word — *color* against *colour* matches
nothing at either level. The section or the member reads as defaults, every step reporting success,
and what happens to the person's value then depends on the store. **One document in sections** keeps
the file's key and writes the build's beside it, so the file holds both spellings while the
application reads the empty one. **One file of one type** is worse: the next write serialises the
type back out, and a key the type does not declare is not in the output, so the person's value is
deleted rather than left orphaned.

So: **copy every name out of the file rather than retyping it**, and check the spelling against what
is on disk before shipping a build that reads it. A section reports a conflicting key if asked. The
watcher can push one instead, through its failure event, but only where the application supplies the
obstruction delegate that asks the store — nothing wires that by default, and a whole-file store has
nothing to report through it.

### The two stores keep different promises about the rest of the file

Both read a file containing comments and trailing commas, and neither ever writes one. What happens
to everything the application did not write is where they part.

| | One document in sections | One file of one type |
|---|---|---|
| A sibling section, or one whose component is gone | Untouched | — |
| A hand-written comment, the key order, the indent, the byte-order mark | Kept | Discarded at the next write |
| A property the type does not declare | Kept | Discarded at the next write |
| A file that cannot be parsed | Copied aside and left where it is — until the next write of any section, which lays down a whole new document and takes the rest of the broken file with it | Copied aside and **overwritten with defaults**, at construction and at every reload that finds it broken |
| An enum member no type answers to | Costs that section | Costs the whole file |

Neither store's tolerance of comments is a property of JSON, so a comment in a document another
program also reads may be unreadable there. Check before putting one in.

Neither store writes on the application's behalf until a load has succeeded. That latch is the
store's own and cannot be switched off, and it is what stops a transient read failure replacing a
good file with defaults. The quarantine repair is not held by it: a whole-file store that finds a
broken file as it is constructed copies it aside and lays down defaults before the latch is ever
set. Only the sectioned store publishes the latch, as `HasLoaded`; with a whole-file store the
refused save is the only way a host hears of it, which is why the save result is worth reading.

A settings class must be a class with a public parameterless constructor, and its properties must be
settable rather than init-only wherever the application changes them through the store. A positional
record does not qualify.

### The migration writes a new file and never touches the old one

It exists to group a flat file into sections, for installations that predate them. The source is
opened read-only and is never written, renamed or deleted, even on success — retiring it is the
application's own step.

An installation whose file is already sectioned has nothing to migrate: point the store at the file
and it reads it as it stands. Running the migration there is not how a section is added — a section
is written by the component that owns it, on first use. The two things it is still good for on such
a file are moving the document somewhere else and raising the version it declares, which a section
write never does. Moving it is not a byte-for-byte copy: the migration writes no comment of its own,
because a reader that disallows comments fails the whole file rather than degrading, so every
comment sitting outside a value is dropped and named on the result instead, and only the old file
still holds them. A comment inside a value travels, because the value is carried as bytes.

**Nothing migrates a consumer's own values.** No component reads a setting the application stored
somewhere else, and none detects that nothing was carried across: a missing value is
indistinguishable from a fresh installation, so the user's broker settings, window rectangle or
update preference simply vanish. Carrying them over is part of adopting, and it happens once.

### Crash dumps need the machine hive

The dump registration writes under the machine hive, because Windows Error Reporting reads local
dump settings nowhere else. An application without elevation cannot arm it — and the refusal comes
back as an exception rather than a false, so the whole step is guarded or skipped. Reading the
registration is safe unelevated; disarming and sweeping are not.

A registration is also not sufficient on its own. **A process whose error mode suppresses the fault
dialog never reaches Windows Error Reporting**, and the mode is inherited: an application launched
from the shell has nothing to clear, while one started under a job shell that suppresses faults —
a build agent's, for instance — inherits the suppression and produces no dump however correct the
registration. An application that must be dumpable under any parent clears its own error mode at
startup. Nothing in the library makes that call.

### The tray host refuses the notify-icon library's efficiency default

The library it is built on arms an efficiency mode by default, which drops the whole process to the
lowest priority class and into a throttled power band, once and for the rest of the run. That is what
slowed both applications. The host turns it off when it creates the icon, unconditionally and with no
option to put it back — measured on the running process from outside, not on the argument passed in.

Efficiency mode is a property of the process rather than of the icon, so **any other notify icon the
application creates with the library's default arms it for everything**, the shared host included.

The host needs no window, but it does need the XAML runtime and a dispatcher on the thread that
starts it.

### The single-instance lock belongs to the thread that takes it

It is held for the life of the process and never released, so it must be taken on a thread that lasts
that long: taken on a thread that ends, it is abandoned, and the next instance takes it as its own.
That is the whole trap — the handle is rooted for the process, the ownership is not.

The name is the application's own and the component takes no position on it beyond refusing a blank
one. A `Global\` name is what the lifecycle guide's wiring uses and what an installer's `AppMutex`
matches, so it is the usual choice; the tests use `Local\` names because they run several instances
of their own side by side.

### A tinted fill over a brand ground is a solid colour, not an opacity

Both brand backgrounds sit near black, so **any brand colour laid over one at reduced opacity lands
near black too** — measured, every one of them below the 3:1 floor a non-text element needs, and the
best of them barely above 1.7:1. White over a ground reaches 2.17:1 and is no better. Use the solid
brush the palette already carries for the colour wanted; the dictionary holds no tint key, because a
tint does not work over these grounds.

The second background is a raised surface rather than a fill: against the first it measures 1.06:1,
which separates two panels and distinguishes nothing on them.

The palette keys live in theme dictionaries, so they resolve as theme resources and not as static
ones. Merge the dictionary in the application's own resources — a merge into a window or a page
resolves for that surface alone.

An application replacing hand-typed palette literals with these brushes **measures first**. At least
one literal in use was matched by eye rather than copied, so a straight swap changes a colour on
screen without anything saying so. Compare each literal against the palette value, and treat a
mismatch as a decision — which of the two is right — rather than a typo to fix in passing.

### The update flow verifies before it executes, and a checksum is not that

Two checks stand between a downloaded installer and running it: its SHA-256 against a hash published
in the release body, and then its Authenticode signature and publisher against a fingerprint the
application pins. Both must pass, and a refusal deletes the whole download and runs nothing. Where no
hash is published, or two different ones are, nothing is downloaded at all.

A checksum file published beside the installer is not the second check and cannot become one. It
comes from the same place as the file, so whatever could replace the file could replace the checksum
with it; all it can answer is whether the bytes arrived whole. Only the signature says who published
them, because the key is what an attacker does not have.

The fingerprint pin is accepted as either a SHA-1 or a SHA-256 thumbprint, with case free and
separators stripped, so whichever form the certificate tool prints can be pasted in as it stands. **A
self-signed certificate makes the thumbprint mandatory**: no machine trusts its root, so the subject
alone proves only that someone put that name on a certificate. The same string serves the
release-verification workflow below.

Publishing the hash is the application's own release workflow's job, and the flow reads it as the one
distinct sixty-four-character hexadecimal run anywhere in the release body — the same run written
twice is fine, two different ones stop the update.

### Cancelling the MQTT panel is final

The panel's cancel abandons a probe that would otherwise outlive the window, and after it the panel
never touches its controls again. It belongs on the window closing and nowhere else: called from a
section's leave hook it leaves a dead page for the next visit, because the settings shell keeps every
page rather than rebuilding it.

## What the application still owns

Its own `app.manifest` decides per-monitor-v2 DPI awareness and which version of the common controls
the process loads — the task dialog exists in version 6 only — and no library can declare either on
an application's behalf. The kit's template declares both. Without the dependency the update
question falls back to a yes-or-no box, which loses the release notes and the way through to the
release page, so it is a smaller offer rather than the same one drawn differently.

An application shipping language-folder resources needs `en-GB` as its default language, because a
merged app PRI is built from the application's own resources and no referenced library can declare
the default on its behalf. The WinUI application block already sets it, so a project importing the
block has it and the line is the application's own only where it does not import the block or wants
another value.

Registering the logon task needs an elevated process, so the manifest that asks for elevation, and
the installer that registers on the user's behalf, stay with the application.

Release verification is taken from this repository's own CI rather than as a reference: a composite
action that fails a tagged run whose signing material is missing, and a reusable workflow that
fetches back what was published and asserts it is what the build produced. Both are called by tag
from this repository, and
[`releasing.md`](releasing.md#verifying-an-applications-release) has the caller's shape.
