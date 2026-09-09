# Replacing an application's own instance lock and relaunch limiter

A checklist for an application that already keeps itself to one process and already brings itself
back — and wants those two pieces to become one shared component. The wiring order, the acquisition
outcome and the settle delay are in [`zerozero-lifecycle.md`](zerozero-lifecycle.md#wiring), and the
reference routes are in [`consuming.md`](consuming.md); neither is repeated here.

## What it does not do

- **A crash is not covered, and adopting this does not cover it.** The component hooks process
  exit, and the runtime raises no exit event for an unhandled exception, so a crash never reaches
  it. Measured here rather than assumed: a test executable arms the hook, throws, and starts nothing
  (`tests/ZeroZero.Lifecycle.Tests/ProcessLifecycleProcessTests.cs:91`). What relaunch covers is the
  clean exit nobody asked for. Whether anything brings a crashed process back — a scheduled task, a
  service, or nothing — is the application's own question, unchanged by this swap and unanswered by
  it. Recording the crash is the diagnostics component's.
- **The relaunch budget is three in ten minutes and cannot be changed.** No public member moves the
  limit or the window. An application whose own limiter used another budget takes this one.
- **Nothing hands out the relaunch decision.** The component writes a line saying what it decided
  and keeps the answer to itself, so an application that showed the reason somewhere reads its log
  instead.
- **The path helper composes the roaming application-data folder under the product name, and only
  that.** The limiter does not: its directory is whatever reaches
  `ProcessLifecycleOptions.DataDirectory`, and `relaunches.txt` goes under that
  (`src/ZeroZero.Lifecycle/RelaunchLimiter.cs:28`). A helper that kept its files in the local folder
  either moves them, keeps its own path, or hands that path in.
- **The lock is never released, and there is no call that releases it.** A helper that dropped its
  mutex on the way out loses that: the handle is rooted for the life of the process on purpose,
  because a release while the process runs is the state the lock exists to prevent.
- **The lock name is taken as written** — no `Global\` prefixing, no access list, no integrity
  level. Only a blank name is refused.

## The checklist

1. Reference `ZeroZero.Lifecycle`. It brings `ZeroZero.Primitives` with it.
2. **Keep the existing mutex name, character for character.** A new name lets an old build and a new
   one run side by side through an upgrade, each holding a name the other never looks at. The name
   in the guide's wiring example stands for the application's own and is not a form to adopt: the
   lock takes what it is given, adds no `Global\` prefix and refuses only a blank
   (`src/ZeroZero.Lifecycle/SingleInstanceLock.cs:66`). Where the example and this instruction seem
   to disagree, this one is right.
3. Put the acquisition and the exit hook in the order the guide's wiring shows, and delete the
   helper's own ordering rather than adapting it. The lock takes no log sink, so the whole order runs
   before the application has a logger.
4. Carry every deliberate-exit mark across. Each place the application exits on purpose — the tray
   menu, the hand-over to an update installer, the installer asking it to close — calls
   `MarkDeliberateExit()` first. One missed call is a relaunch after an exit somebody asked for.
5. Delete the helper's own limiter and its count file. Nothing carries the old count over, and
   starting from an empty budget is the right answer: the new file is `relaunches.txt` in the product
   data folder.
6. Leave whatever recovers a crashed process, if anything does, and the application's own judgement
   of what counts as a deliberate exit, exactly as they are.
7. **Decide where the acquisition outcome is recorded, because nothing here records it.** `Acquire`
   returns the outcome and writes nothing, and the order above puts it before the logger — so a
   second copy that is refused leaves no line in the log and nothing in the crash log,
   which reads to a person as an application that does not start and says why nowhere. Adopting this
   component neither causes that nor fixes it: either a logger opens early enough on the refusal path
   to write the outcome, or a silent refusal is accepted as the behaviour. Recorded, the outcome is
   worth more than a helper's true or false — it separates a free name from one a dead instance left
   behind.

## Verify

- A second copy started while the first runs exits with no tray icon and starts nothing.
- Exit from the tray menu: nothing comes back.
- End the process from Task Manager: nothing in this component sees that exit, so nothing here
  brings it back. Whatever did before the swap still does, and whatever did not still does not.

The guide's three traps apply unchanged — the refusal path arming nothing, the thread that owns the
lock, and the two refusals meaning different things.
