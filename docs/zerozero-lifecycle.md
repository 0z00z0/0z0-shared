# The lifecycle component

`ZeroZero.Lifecycle` keeps a tray application one process that comes back: the single-instance
lock held for the life of the process, the deliberate-exit mark and the relaunch on any other clean
exit under a sliding-window limit, and the per-user data path every other file hangs off. Plain
`net10.0`; one project reference, `ZeroZero.Primitives`, for the log sink; one package,
`Microsoft.Win32.SystemEvents`, for the session-ending signal. No window, no message pump and no
user interface: it is armed before any of those exist.

The assembly is versioned as `LifecycleVersion` in `Versions.props` and released under
`lifecycle-v<x.y.z>` tags, with notes under `docs/release-notes/lifecycle/`;
[`releasing.md`](releasing.md) has the procedure. It references the primitives foundation, so it
releases after `primitives` is on the feed at the version it references.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows. The assembly targets plain `net10.0` and declares itself Windows-only through `SupportedOSPlatform`, with no version: nothing here needs a build floor, and the project states none. An application taking it alongside the WinUI components inherits their floor, not one from here. |

## What it contains

- **`ProductDataPath`** — `Root(product)` is the roaming application-data folder under the product
  name, created if absent so a path composed from it is writable at once; `Under(product, relative)`
  composes a path beneath it and refuses a rooted one rather than silently replacing the folder.
  Depends on nothing else here, so a log file can be placed before anything else exists.
- **`SingleInstanceLock`** — `Acquire(name, wait)` takes the named mutex, waiting up to `wait` for a
  previous instance to let go, and holds it until the process dies. Nothing releases it. It answers
  a `SingleInstanceOutcome`: `TakenFree` where nobody held the name, `TakenAbandoned` where the
  previous holder died without releasing (the case relaunch exists for, and the lock is taken all
  the same), `RefusedHeld` where another instance still held it when the wait ran out, and
  `RefusedDenied` where the name exists under rights this process does not have — another session's
  instance, or one running elevated. `outcome.IsTaken()` is the two taken outcomes and nothing else.
  `TryAcquire(name, wait)` is the same acquisition as a plain true or false. A second call under the
  same name is the lock already held and reports the outcome it was taken with; a second name
  throws, because a process is one instance of one product. `IsHeld` says whether this process holds
  it.

  **A refusal is answered, a broken name is thrown.** An access error becomes `RefusedDenied`,
  because a name held under rights this process lacks is another instance by any other description
  and the right answer is not to run. Every other failure — a name too long, a semaphore or an event
  already under that name — comes out as the exception it is, because a name that can never be a
  mutex is the application's own error, and answered as a refusal it would look like an ordinary
  second instance for ever. Nothing is caught into a taken outcome: the lock cannot fail open.
- **`Relaunch`** — `Argument`, the `--relaunched` the exit hook starts the executable with;
  `WasRelaunched(args)`; and `SettleDelay`, ten seconds, the wait a relaunched process gives its
  parent to finish exiting before it gives up on the lock.
- **`RelaunchLimiter`** — three relaunches in ten minutes, counted through `relaunches.txt` in the
  product's data folder: one ISO 8601 round-trip UTC timestamp per line, in the invariant culture.
  The count is on disk because the process keeping it is the one that keeps dying. A file that
  cannot be read or written allows the relaunch and logs the failure: a tray that never comes back
  costs more than one that comes back once too often. A line that does not parse is dropped.
- **`ProcessLifecycle`** — takes the options and the command line; `IsRelaunch` says whether a
  previous instance's exit hook started this process. `Arm()` hooks process exit and session
  ending, once. `MarkDeliberateExit()` says the exit about to happen was asked for. On any process
  exit the hook decides, in this order: a deliberate exit starts nothing; an exit while Windows is
  logging off or shutting down starts nothing; an exit past the limiter's budget starts nothing;
  anything else starts the executable again with the relaunch argument. A deliberate exit and a
  session ending each log a sentence saying so; a relaunch logs what it started, and the budget
  having run out is already the limiter's own line. The decision is the component's own: nothing
  hands it out, so an application that wants to know why it came back reads the log.

**A crash never reaches the hook.** The runtime raises no exit event for an unhandled exception,
so relaunch covers the clean exit nobody asked for — a message loop that ended, an exit path taken
by mistake — and not the crash. The crash is the diagnostics component's to record
([`zerozero-diagnostics.md`](zerozero-diagnostics.md)) and the application's own watchdog task's
to recover from.

**The relaunched process inherits the token.** An elevated application comes back elevated with no
prompt, and nothing here asks for an elevation the parent did not have.

## Wiring

Before any window or tray icon exists, on the main thread, in this order: the lock first, and the
exit hook only once the lock is taken.

```csharp
string data = ProductDataPath.Root("Product");
bool relaunched = Relaunch.WasRelaunched(args);

SingleInstanceOutcome outcome = SingleInstanceLock.Acquire(
    @"Global\Product.SingleInstance",
    relaunched ? Relaunch.SettleDelay : TimeSpan.Zero);

if (!outcome.IsTaken()) return;   // another instance holds it, or it is not this process's to take

// Only now, with the process established as the instance: the log, then the hook.
ILogSink log = /* the application's own */;
var lifecycle = new ProcessLifecycle(new ProcessLifecycleOptions { DataDirectory = data, Log = log }, args);
lifecycle.Arm();

log.Info(outcome == SingleInstanceOutcome.TakenAbandoned
    ? "Started after an instance that did not exit cleanly."
    : "Started as the only instance.");
```

Then, everywhere the application exits on purpose — the tray menu's exit, before an update
installer runs, when the installer asks it to close — `MarkDeliberateExit()` first.

**Nothing constructed is needed to reach the lock.** `ProductDataPath` and `Relaunch` are static and
depend on nothing, and the lock takes no log sink, so the whole of the order above runs before the
application has a logger. That is why the outcome is a value the application carries rather than a
line the component writes: the acquisition happens before there is anything to write with, and the
sentence is written a few lines later once there is. `lifecycle.IsRelaunch` answers the same question
as `relaunched` afterwards, for anything that needs it later.

**On the refusal path there is no logger yet.** An application that wants to say why it stopped
builds one before returning — the data folder is already known by then — or says nothing. Either
way nothing is armed, so the refusal starts nothing.

### What the order costs

The hook is armed after the lock, so **a clean exit between the process starting and the lock being
taken reaches no hook and is not relaunched.** What actually exits in that window is the refusal,
and a refusal must never relaunch: a relaunched second instance finds the lock held again, exits
again, and goes round until the limiter stops it. The other ways out of the window are not clean
exits at all — an unusable data folder or a name that cannot be a mutex throws, and a crash raises no
exit event, so the hook never saw either however early it was armed. Anything the application itself
puts in front of the lock — a version switch, a command line it refuses — exits without being
relaunched, which is what it should do.

## What stays in the application

- **What counts as deliberate.** The component marks nothing on its own.
- **The wait per launch kind.** Zero for a launch by a person or the scheduler; `SettleDelay` for a
  relaunch; anything else the application decides.
- **The mutex name.** It is the application's public identity to its installer — the name an Inno
  Setup `AppMutex` directive checks — so it is chosen there, prefix included.
- **The product name** the data folder takes.
- **What to say about the acquisition.** The component answers which of the four outcomes happened
  and writes nothing: the log it would write to does not exist yet at that point.
- **The watchdog task** that brings a crashed application back. The crash itself — the handlers,
  the crash line and the dump registration — is the diagnostics component's.

## Traps

- **The refusal path arms nothing, and must stay that way.** A process that finds the lock held
  returns before `Arm()`, so its exit reaches no hook and needs no mark. Arm before the lock instead
  and the refusal becomes an unmarked clean exit: the hook starts a third instance, which finds the
  lock held, and so on until the limiter stops it. `MarkDeliberateExit()` on the refusal path is
  then required — a requirement that only exists because of the arming that created it.
- **The lock belongs to the thread that took it.** Take it on the thread that lives as long as the
  process. A thread that ends while owning the mutex abandons it, and the next instance takes an
  abandoned mutex as its own.
- **A refused acquisition is not always a second instance.** `RefusedDenied` says the name exists
  and this process may not open it, which on a machine with one user is usually a name clash with
  something else rather than another copy of the application. An application that reports both
  refusals with the same sentence hides a mutex name it can never take.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Lifecycle`; it brings
`ZeroZero.Primitives` with it.

The tests are in `tests/ZeroZero.Lifecycle.Tests`, plain `net10.0`, and run on Windows only. They
take real named mutexes under `Local\ZeroZero.Lifecycle.Tests.<guid>`, with the other instance
played by a thread of its own, and one created through the platform call with an empty protected
access list so that nobody — the creating process included — may open it, which is the only way to
reach the denied refusal without a second account; keep their files in folders under the temporary
folder, and one folder per test under the roaming folder, all removed afterwards; and provoke the
exit hook for real: the test executable's own entry point wires itself against a folder the test
owns and exits as told, and the test reads from outside that process whether the executable came
back with the relaunch argument, or did not. Two of those scenarios pin the order this guide
prescribes — a refused instance starts nothing because nothing was armed, and a process that arms
the hook and then dies of an unhandled exception starts nothing either.

The public path is exercised in one test rather than several: the lock it takes is process-wide and
held for the rest of the run, so a second public-path test would answer differently depending on
which of the two ran first.
