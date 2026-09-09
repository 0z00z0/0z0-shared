# The startup component

`ZeroZero.Startup` is the shareable slice of automatic startup: the application's logon task in the
Task Scheduler — the identity it runs as, the power-safe elevated definition, registration, the
direct enabled read, enable and disable, deletion, the repair of a task an older build registered,
and a demand start that proves the task can run. Plain `net10.0`; one project reference,
`ZeroZero.Primitives`, for the log sink; one package, the `TaskScheduler` library, of which no type
reaches a public signature. No user interface, no window, no message pump.

**It is a slice, deliberately.** Automatic startup and elevation are one decision: the task runs at
the highest level on the interactive token because the application's manifest requires
administrator, and a registry run key would either fail to start such an application or prompt at
every logon. What makes that decision stays with the application, and this assembly does not reach
for it:

- **The application manifest** — a build input, and requiring administrator is a per-application
  product decision.
- **The installer script** — registering the task, the checkbox that offers it, and the launch,
  kill and uninstall choreography. Inno Setup Pascal cannot reference a package.
- **The install-directory gate** that stops a development build registering a task pointing at
  build output.
- **The watchdog task**, where an application has one — its trigger set, relaunch argument, hold
  marker and retry budget are that application's resilience design.

**Nothing here creates the logon task.** Whether the application runs at logon is the user's
choice, made in the installer or in the application's settings, and `Repair` leaves an absent task
absent.

The assembly is versioned as `StartupVersion` in `Versions.props` and released under
`startup-v<x.y.z>` tags, with notes under `docs/release-notes/startup/`;
[`releasing.md`](releasing.md) has the procedure. It references the primitives foundation, so it
releases after `primitives` is on the feed at the version it references.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows. The assembly targets plain `net10.0` and declares itself Windows-only through `SupportedOSPlatform`, with no version: nothing here needs a build floor, and the project states none. An application taking it alongside the WinUI components inherits their floor, not one from here. |
| Token | Registering or repairing needs an elevated process: the scheduler refuses a highest-run-level task from a standard token, with an access-denied error. Reading, enabling, disabling, deleting and demand-starting work from any token that owns the task. |
| Globalisation | The consuming application must not set `InvariantGlobalization`. Every write through the scheduler library then fails with a type-initialisation error while every read still works, so the failure looks like a permissions problem. |

## What it contains

- **`TaskIdentity`** — `Current()`: the account name, which the logon trigger takes, and the
  security identifier, which the principal takes. The scheduler accepts neither in the other's
  place.
- **`StartupTaskOptions`** — `TaskName`, the task's name in the scheduler's root folder and its
  public identity, which the installer's registration and uninstall must match; `Description`;
  `ExecutablePath`, the running executable when null; `Arguments`; `VerifyByDemandStart`; `Log`.
- **`StartupTask`** — the task by name. `IsEnabled` fetches the task directly rather than walking
  the folder, because it is read on every refresh of a tray menu. `Read()` is the whole state.
  `Register()` writes the task as defined below, replacing any of the name. `Enable()` and
  `Disable()` write through, and throw `InvalidOperationException` when no task is registered: the
  user asked, and a silent no-op would leave the menu showing a change that did not happen.
  `Delete()` removes the task and says whether there was one. `Repair()` is the repair below.
  `DemandStart(wait)` starts the task now and waits for the scheduler to report the run; what it
  waits for is [below](#what-a-demand-start-waits-for).
- **`StartupTaskState`** — `Exists`, `Enabled`, `LastRun`, `LastResult` and **`HasEverRun`**. The
  last is the one that matters: a task can exist and be enabled and never once have started the
  executable, and the first two facts say nothing about the third. The scheduler reports
  `0x41303` as the last result of a task that has never run; `StartupTask.NeverRunResult` names it,
  `StartupTask.RunningResult` names the `0x41301` it reports while a run is in flight, and
  `StartupTask.AlreadyRunningResult` names the `0x800710E0` it records when it refuses a start
  because an instance is already alive.
- **`StartupTaskRunResult`** — what a demand start came to: whether the run ended within the wait,
  when, with what code, and whether the task was still running when the wait ended. `Succeeded` is
  either a run that ended with zero or a task still running, because a program that stays resident
  never exits and waiting for an exit code would fail every task that starts one.
- **`StartupTaskRepair`** — the repair decision over delegates, so the decision is testable
  without a scheduler, and the outcomes: `NotRegistered`, `AlreadyCorrect`, `Repaired`,
  `RepairFailed` and `VerificationFailed`. No failure in a delegate escapes it — a scheduler that
  refuses, a task that vanishes mid-repair, a verification that throws — because it runs at
  application start, where a throw would take the application down over a task it never needed to
  be running. A delegate passed as null is the one exception, and it is an argument error rather
  than a failure: nothing has run yet. `StartupTask.Repair()` keeps the same promise end to end,
  the current identity read and the state logged afterwards included.

### The definition

A logon trigger for the account; the principal by security identifier, interactive token, highest
run level; the executable as the action, in its own folder, with the arguments given; and these
settings, because the scheduler's defaults are for a maintenance job rather than a resident
application:

| Setting | Value | Why |
|---|---|---|
| Start only on mains power | off | A machine booting on battery never got the application while the scheduler reported success. |
| Stop when going on battery | off | The same incident. |
| Allow hard terminate | off | The application is not to be killed by the scheduler. |
| Execution time limit | none | The default is three days, after which the scheduler kills the process silently. |
| Multiple instances | ignore new | The application is single-instance. |
| Run only when idle | off | |
| Priority | normal | The default is below normal. |

Whether the task is enabled is not part of the definition: `Register()` enables it, and `Repair()`
keeps whatever the user set.

### What a demand start waits for

The scheduler settles a run's state before it settles the run's result, and between the two it
writes the code that means the launch succeeded — zero. A reader that stops as soon as the state
is no longer "running" and reads the result at that instant can therefore be handed the launch's
zero instead of the executable's exit code, and a run counts as succeeded when its code is zero.
The same gap is open wider while a run is merely queued: the last run time has already moved on
while the result still holds the previous run's.

So the wait takes all three readings at every poll, and:

- a run is over only at state `Ready`; queued is not over;
- `0x41301` and `0x41303` are never a run's result;
- any other non-zero code is the executable's, and is final at once;
- a zero is held for `StartupTask.ResultSettle`, 500 ms, and believed only if it survives. The
  measured worst case between the state settling and the result settling is 72 ms.

The settle is paid only where the result is zero, and the wait the caller gave stays a hard bound:
a run that ends with zero inside its last 500 ms is reported as not ended rather than as succeeded.

Nothing settles at all where the started program stays resident, because such a run never ends. So
one further reading is taken once the wait is over, and a task still running then, whose run time
has moved past the reading taken before the start, counts as a program that started. The scheduler
reports the task as running in two shapes and both count: `0x41301`, where it launched the program,
and `0x800710E0`, where it refused the start because an instance was already alive. Queued is not
running, so a run that never started is still not a successful one, and a task that cannot start
what it points at never reaches running at all — it returns to ready carrying the error, in under a
tenth of a second.

There is no early return while the task runs, and the wait's length is the only thing that separates
a resident program from a finished one. **The scheduler holds a finished run in the running state
for seconds after the program is gone** — measured 4.0 to 8.1 s on an idle machine, a median of
6.1 s and a worst case of 48 s with every core loaded — and during that window the state, the
instance count and the result code all read exactly as they do for a program that is genuinely up.
`StartupTask.VerificationWait` is 15 s, about twice the worst idle measurement: a run that fails
inside the wait and is reaped before it ends is reported as the failure it is, and one the scheduler
has not yet reaped is reported as a start.

### The repair

At every start, the application asks `Repair()` to bring a task an older build registered up to
the definition above. It reads the task, lists every way it differs — a setting the scheduler
would have defaulted, a run level or logon type, a missing logon trigger, an executable or
arguments other than the current ones — and rewrites the whole definition when anything does,
keeping the enabled flag as it found it. With `VerifyByDemandStart`, a rewritten task is then
started on demand and the outcome is `Repaired` only if that start succeeded — the run ended with
zero, or the task is still running when the wait ends. The state of the task is logged afterwards
either way, with the last run and its result, so a log never says "registered and enabled" about a
task that has never run.

Verification costs the whole wait wherever the started program stays resident, and that is the
common case: it is paid once, on the start after an upgrade that changed the definition, since a
task that already matches is never rewritten and never verified. The verification stops nothing it
started. Where the scheduler refused the start, there is nothing to stop; where it launched the
program, the copy it started is a legitimate one, and the two readings are the same, so stopping on
that reading would risk killing a running application.

## Wiring

At start, on the elevated process:

```csharp
using var task = new StartupTask(new StartupTaskOptions
{
    TaskName = "Product",
    Description = "Starts Product at logon.",
    Log = log,
});
StartupTaskRepairResult repair = task.Repair();
```

In the tray menu, `task.IsEnabled` for the check mark and `task.Enable()` or `task.Disable()` on
the click, catching `InvalidOperationException` as "not installed with a startup task". The
installer registers the task under the same name, with the same settings, and removes it at
uninstall; that script cannot call this assembly, so the two definitions are kept in step by hand.

`VerifyByDemandStart` works on the application's own logon task. A demand start of it from the
running application is refused by the scheduler, because the definition's multiple-instance policy
is ignore-new and the application is the instance, and that refusal counts as a start. The one case
it still gets wrong is an application started outside its task while the task itself has no running
instance: the scheduler then launches a second copy, the single-instance lock turns it away, and the
exit code the application gives that case is reported as a failed verification.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Startup`; it brings
`ZeroZero.Primitives` and the scheduler library with it.

The tests are in `tests/ZeroZero.Startup.Tests`, plain `net10.0`, and run on Windows only,
against the real scheduler: tasks named `ZeroZero.Startup.Tests.<guid>` in the root folder, each
starting the command interpreter with an exit code, a program that stays up, or a path no file
occupies, deleted when the test ends and swept at the start of a run. From a standard token the
disposable task is registered at the standard run level, and the tests prove the read, enable,
disable, delete, the demand start with exit codes zero and seven, a program that stays resident, a
start the scheduler refuses because an instance is alive, an executable that does not exist,
repair's refusal as an outcome, and that the highest level is refused. Four tests need an elevated
process — registration at the highest level, repair rewriting an older build's settings while
keeping the task disabled, and verification by demand start both ways — and are skipped, and
reported as skipped, from a standard token.

`DemandStartDecisionTests` covers the same decisions with no scheduler, which is the only way to
reach the queued state on demand: the scheduler passes through it too briefly to be caught, and it
is the state that must not count as a start.
