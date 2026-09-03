# The diagnostics component

Crash diagnostics: what a process does with an exception nothing caught, the file that crash reaches
when everything else has failed, the version line that says which build wrote the log, and the
Windows Error Reporting registration that turns a crash into a dump file. Two assemblies, plain
`net10.0`, no third-party package: `ZeroZero.Diagnostics` is the entry point and carries
`ZeroZero.Diagnostics.Dumps` with it. Both take `ZeroZero.Primitives` for the log sink and the version
reader, and nothing else.

The component is versioned as `DiagnosticsVersion` in `Versions.props` and released under
`diagnostics-v<x.y.z>` tags, with notes under `docs/release-notes/diagnostics/`;
[`releasing.md`](releasing.md) has the procedure. It references the primitives foundation, so it
releases after the primitives version it references is on the feed.

**What stays with the application, by design:** the logging framework and its configuration — the
component writes through the two-member `ILogSink` and dictates nothing about files, rotation or
levels; the UI framework's own unhandled-exception event and its `Handled` decision; where the armed
flag is kept — a marker file, a setting, a command line — and the order of startup. Dump type,
retained count, the dump directory and the hive are parameters with no default, because the two
applications that have this today chose differently and a default would quietly override one of
them.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | `ZeroZero.Diagnostics` runs anywhere .NET 10 does. `ZeroZero.Diagnostics.Dumps` declares itself Windows-only: it writes the Windows Error Reporting registry key and nothing else. |
| Elevation | Arming a dump registration writes the machine hive, which needs an elevated process. Windows Error Reporting reads local dump settings from `HKEY_LOCAL_MACHINE` alone; a registration under the current user's hive is accepted by the registry and produces no dump (measured: three crashes, no file). |
| Error mode | A process whose error mode carries `SEM_NOGPFAULTERRORBOX` never reaches Windows Error Reporting, so no registration produces a dump for it. The mode is inherited from the parent: an application launched by the shell has mode 0, while a build server's job shell runs with the flag set (measured: 0x8003 on the hosted runner) and hands it to every child. A process that must be dumpable under such a parent clears it with `SetErrorMode(0)`. |

## What it contains

`ZeroZero.Diagnostics`:

- **`StartupVersionLine`** — `For(assembly)` is `Name 1.2.3+commit starting`, the version being the
  full text the assembly carries through `AssemblyVersionText.Read`, commit whole, never the About-box
  form. `Write(sink, assembly)` sends it as one `Info`. Write it first, before anything that can
  throw, through a sink that writes regardless of level: a log that starts with no version line is
  the origin of this component.
- **`CrashHandlers`** — `Register(options)` wires `AppDomain.UnhandledException` and
  `TaskScheduler.UnobservedTaskException`; `Report(source, exception)` is what the host's own arm
  calls, so all three land in the one place. Each crash goes to the crash line first, because that
  never throws, and then to the host's sink, guarded, because a sink that fails while reporting a
  crash would hide it. A reported unobserved task exception is marked observed. Disposing unwires
  both arms, which only a test host needs.
- **`CrashHandlerOptions`** — `Sink`, the host's log, required; `CrashLine`, a `CrashLineAppender` or
  null.
- **`CrashLineAppender`** — one stamped entry appended to a plain text file: the local time with its
  offset, the source, the exception's type and message, then the whole exception beneath — stack and
  inner exceptions — because the dump may never be read and the entry is then all there is.
  `Append` answers false and throws never: a locked file, a path that turns out to be a file, a drive
  that is gone, all lose the entry rather than the crash. Construction validates the path and may
  throw. It is an `ILogSink`, so it also serves as the sink before the host's logging exists.

`ZeroZero.Diagnostics.Dumps`:

- **`DumpPolicy`** — executable name (the image file name, `MyApp.exe`, as Windows Error Reporting
  keys it), dump directory (a full path, or one starting with an environment variable, which Windows
  Error Reporting expands itself), `DumpType`, retained count. Four parameters, one constructor, and
  a value Windows Error Reporting could not act on is refused where it is written.
- **`DumpType`** — `Mini` (stacks, no heap: a stowed exception's type and message are absent) or
  `Full` (the whole process memory). The values are Windows Error Reporting's own.
- **`DumpRegistration`** — over a hive (`Registry.LocalMachine` for a process that runs elevated) and
  a log. `Arm(policy)` writes `DumpFolder`, `DumpCount` and `DumpType` under
  `LocalDumps\<executable>`; `Disarm(name)` removes the key; `Apply(policy, armed)` does one or the
  other according to the flag the application holds; `IsArmed` and `Read` say what is registered.
  `RemoveResidue(names)` removes the registrations older builds left under other names — the names
  are the application's history and arrive as parameters. Every removal ends with
  `RemoveRootIfEmpty`: the shared `LocalDumps` key is deleted once it holds no registration and no
  value, because its mere existence turns dump collection on for every process on the machine, at
  the defaults. A registry refusal is thrown, not hidden.
- **`DumpRetention.Prune(directory, executable, keep, log)`** — deletes the oldest `<executable>.<pid>.dmp`
  files beyond `keep`. Windows Error Reporting bounds the count too, but only for the registration it
  currently holds; a lowered count, a disarmed executable and an older build's name all leave files
  it never touches again. A file that will not delete is logged and left.

## Wire it

In this order, at the top of the application's entry point:

1. **The version line.** `StartupVersionLine.Write(sink, typeof(App).Assembly)`, through a sink that
   writes regardless of level, before anything that can throw.
2. **The handlers.** `CrashHandlers.Register(new CrashHandlerOptions { Sink = sink, CrashLine = new CrashLineAppender(path) })`,
   holding the result for the life of the process. The crash-line path is under the application's
   own data directory; the appender creates the directory on first write.
3. **The UI arm.** In the framework's unhandled-exception handler call `handlers.Report("Application.UnhandledException", e.Exception)`,
   then decide `Handled` as the application always has.
4. **The dumps**, in an elevated process only: `new DumpRegistration(Registry.LocalMachine, sink)`,
   then `RemoveResidue` with the names of older builds, then `Apply(policy, armed)` with the
   application's own policy and its own flag. An unelevated process skips this step; disarming and
   sweeping also write the machine hive.
5. **Retention**, at startup or after a disarm: `DumpRetention.Prune(policy.DumpDirectory, policy.ExecutableName, policy.RetainedCount, sink)`,
   with the directory expanded by the application if the policy names it through a variable.

The two-member sink has no level. The version line survives level settings because the sink the
application hands to step 1 is one that writes unconditionally; the component cannot make that
choice for it.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Diagnostics`; it carries
`ZeroZero.Diagnostics.Dumps` and `ZeroZero.Primitives` with it. A headless consumer that wants the
registration alone takes `ZeroZero.Diagnostics.Dumps`.

The tests are in `tests/ZeroZero.Diagnostics.Tests` and `tests/ZeroZero.Diagnostics.Dumps.Tests`,
plain `net10.0`, Windows only for the second. Each test assembly is its own crash victim: it carries
an entry point the tests launch as a separate process, so an unhandled exception is genuinely
unhandled, a dropped task is genuinely finalised, and what the handlers wrote is read back from disk.
The registry lifecycle runs against a real key under the current user's hive, under a scratch path,
never where Windows Error Reporting reads. The one test that provokes a real dump under a real
machine-hive registration needs an elevated process and is skipped, with the reason, without one;
its victim clears the error mode it inherited before crashing, so it is dumpable under a build
server's job shell as well as under the shell a user launches from.
