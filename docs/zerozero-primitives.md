# The primitives foundation assembly

`ZeroZero.Primitives` holds the pieces several components need and none of them owns: the
two-member log sink and its no-op, the reader of the version an assembly reports and the form an
About box shows, the coalescing gate, and the source-revision stamp as build properties and targets.
Plain `net10.0`, no package references, no project references, and no domain vocabulary — which is
what makes it **foundation** rather than a component: any component may take it. The MQTT module
does, for its log sink and for the gate under every retained channel; the diagnostics component
does, for the sink and for the version reader beneath its startup line; the update component does,
for the sink and for the version reader behind the running version; and the lifecycle and startup
components do, for the sink.

The assembly is versioned as `PrimitivesVersion` in `Versions.props` and released under
`primitives-v<x.y.z>` tags, with notes under `docs/release-notes/primitives/`;
[`releasing.md`](releasing.md) has the procedure. A component that references it can only release
once the version it references is on the feed, so a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Any .NET 10 target; nothing Windows-specific. |

## What it contains

- **`ILogSink`** — `Info(message)` and `Error(source, exception)`, and nothing else. The host owns
  the logging framework; a component holds a sink. **`NullLogSink.Instance`** is the one shared
  no-op every component defaults to, so a host that supplies nothing still runs, and a host can tell
  "nothing wired" by identity. The exception handed to `Error` may be null: a component reports a
  refusal with a message alone. A component that handles a credential sanitises before it calls —
  type and message only — so nothing secret reaches the host's log through this seam. Every
  component is typed on this interface directly; no component carries an alias of it under its own
  name.
- **`AssemblyVersionText.Read(assembly)`** — the version the assembly carries: the informational
  version where one is stamped (`0.7.0+1a2b3c4`), the assembly version otherwise, and the empty
  string where the assembly carries neither — never a fabricated number. Read off the loaded assembly
  rather than compiled in from a constant, so a build made from a working tree between tags says
  which source produced it; a constant from the same property the pin is written against can never
  disagree with the pin, and disagreeing is the point.
- **`AssemblyVersionText.ForDisplay(version)`** — the same text with a commit after the `+` cut to
  seven characters. The SDK's own stamp is the full forty, which no About box has room for.
  Metadata that is not a commit, and a version carrying none, pass through untouched. An About box
  fed from the brand component's `AboutInfo` takes `ForDisplay(Read(typeof(App).Assembly))` as its
  version.
- **`CoalescingGate`** — collapses a burst of signals into one in-flight pass plus at most one
  trailing pass, so the last signal always gets a pass and a burst of any size costs two. `Signal()`
  returns true only to the caller that must start the loop; the loop calls `BeginPass()` before each
  pass and `ShouldRepeat()` after, and ends when that answers false. It tracks two flags and holds no
  thread and no timer, so the coalescing decision is testable without either.
- **The source-revision stamp** — `build/ZeroZero.Primitives.props` and
  `build/ZeroZero.Primitives.targets`. The targets file sets `SourceRevisionId` to the short
  seven-character commit of the consuming project's own repository, so `AssemblyInformationalVersion`
  reads `<version>+<commit>` and `Read` returns exactly that; without it the SDK's own stamp is the
  full forty characters. The commit is read from the consuming project's directory, never from the
  file's own, which as a package sits in the package cache and in no repository. A tree with no git
  available stamps nothing and the assembly reports the bare number.

## Traps

ChargeKeeper measured these against the logging framework it wires behind the sink. They are
properties of a logging framework behind `ILogSink` generally, not of that one host.

- **Several writers to one shared log file lose entries silently unless two settings are
  combined.** Measured: roughly seventy lines lost with neither set, one to three lost with only
  one of them, none lost with both. The setting that most obviously looks like the fix no longer
  exists in the framework's current major version and is accepted without complaint, so a
  configuration that reads as correct can still be doing nothing.
- **Archiving logs by age can delete an entire history in one write.** An archived file keeps its
  original creation time across the rename, so a file that stayed open for weeks is judged too old
  the moment it finally rolls over, and the whole archive goes in a single retention pass — measured
  against real files on disk, not by reading the framework's configuration. Keep a count of
  archives instead of an age.
- **Asking at write time which line of code wrote an entry costs more than letting the compiler
  supply it.** Measured at 2.2 times the cost of the compiler-supplied caller information, which
  costs nothing.
- **A packaged application can be handed a frozen copy of its own log file by the operating
  system's container mechanism.** One measured case was three weeks stale while the file looked
  entirely normal. Confirm a read through a route that bypasses the container view before trusting
  what the file says.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Primitives` itself;
there is nothing beneath it. An application taking a component that already references it — the
MQTT module does — has the types transitively and adds nothing for them.

**The stamp is taken by a direct reference.** On the package route NuGet imports the two build
files into the project that references the package, and nothing else is needed. A transitive
package reference — through the MQTT module, say — does not import them, so a component that takes
this assembly never restamps the application above it unasked. On the sibling-checkout route a
project reference carries no build files at all; a consumer that wants the stamp imports the targets
file by path from its own project:

```xml
<Import Project="$(ZeroZeroSharedDir)\src\ZeroZero.Primitives\build\ZeroZero.Primitives.targets" />
```

The props file only states `IncludeSourceRevisionInInformationalVersion`, which is the SDK's default
already; the targets file is the stamp.

The stamp is not part of the [build kit](zerozero-build.md), although both are build files: it is
the build half of `AssemblyVersionText`, so it ships with the assembly that reads it and the one
reference delivers a working version display. The kit neither duplicates nor imports it, and this
repository's `Directory.Build.props` imports both, each from its own folder.

The tests are in `tests/ZeroZero.Primitives.Tests`, plain `net10.0`, and run on any machine with the
SDK: no desktop, no broker, no network.
