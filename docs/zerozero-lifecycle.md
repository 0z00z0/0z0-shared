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
| Platform | Windows 10 1809 (build 10.0.17763) or later. The assembly declares itself Windows-only. |

## What it contains

- **`ProductDataPath`** — `Root(product)` is the roaming application-data folder under the product
  name, created if absent so a path composed from it is writable at once; `Under(product, relative)`
  composes a path beneath it and refuses a rooted one rather than silently replacing the folder.
  Depends on nothing else here, so a log file can be placed before anything else exists.
- **`SingleInstanceLock`** — `TryAcquire(name, wait)` takes the named mutex, waiting up to `wait`
  for a previous instance to let go, and holds it until the process dies. Nothing releases it. A
  mutex its holder abandoned counts as taken: the holder is dead, which is the case relaunch exists
  for. A second call under the same name is the lock already held; a second name throws, because a
  process is one instance of one product. `IsHeld` says whether this process holds it.
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
  anything else starts the executable again with the relaunch argument. Each decision is logged as
  a `RelaunchDecision`.

**A crash never reaches the hook.** The runtime raises no exit event for an unhandled exception,
so relaunch covers the clean exit nobody asked for — a message loop that ended, an exit path taken
by mistake — and not the crash. The crash is the diagnostics component's to record
([`zerozero-diagnostics.md`](zerozero-diagnostics.md)) and the application's own watchdog task's
to recover from.

**The relaunched process inherits the token.** An elevated application comes back elevated with no
prompt, and nothing here asks for an elevation the parent did not have.

## Wiring

Before any window or tray icon exists, on the main thread, in this order:

```csharp
string data = ProductDataPath.Root("Product");

var options = new ProcessLifecycleOptions { DataDirectory = data, Log = log };
var lifecycle = new ProcessLifecycle(options, args);
lifecycle.Arm();

if (!SingleInstanceLock.TryAcquire(@"Global\Product.SingleInstance",
                                   lifecycle.IsRelaunch ? Relaunch.SettleDelay : TimeSpan.Zero))
{
    // Another instance is running. This exit is deliberate: say so, or the hook starts a third.
    lifecycle.MarkDeliberateExit();
    return;
}
```

Then, everywhere the application exits on purpose — the tray menu's exit, before an update
installer runs, when the installer asks it to close — `MarkDeliberateExit()` first.

## What stays in the application

- **What counts as deliberate.** The component marks nothing on its own.
- **The wait per launch kind.** Zero for a launch by a person or the scheduler; `SettleDelay` for a
  relaunch; anything else the application decides.
- **The mutex name.** It is the application's public identity to its installer — the name an Inno
  Setup `AppMutex` directive checks — so it is chosen there, prefix included.
- **The product name** the data folder takes.
- **The watchdog task** that brings a crashed application back. The crash itself — the handlers,
  the crash line and the dump registration — is the diagnostics component's.

## Traps

- **The second instance's exit must be marked deliberate.** A process that finds the lock held and
  returns without `MarkDeliberateExit()` exits clean and unmarked, and the hook it armed starts a
  third instance, which finds the lock held, and so on until the limiter stops it.
- **The lock belongs to the thread that took it.** Take it on the thread that lives as long as the
  process. A thread that ends while owning the mutex abandons it, and the next instance takes an
  abandoned mutex as its own.
- **Arm before the lock.** A relaunch that fails to take the lock exits, and that exit must reach
  the hook to be marked; a hook armed after the lock misses it.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Lifecycle`; it brings
`ZeroZero.Primitives` with it.

The tests are in `tests/ZeroZero.Lifecycle.Tests`, plain `net10.0`, and run on Windows only. They
take real named mutexes under `Local\ZeroZero.Lifecycle.Tests.<guid>`, with the other instance
played by a thread of its own; keep their files in folders under the temporary folder, and one
folder per test under the roaming folder, all removed afterwards; and provoke the exit hook for
real: the test executable's own entry point arms the lifecycle against a folder the test owns and
exits as told, and the test reads from outside that process whether the executable came back with
the relaunch argument, or did not.
