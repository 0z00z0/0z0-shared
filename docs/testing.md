# Testing

Every test stays in the suite, and a test run happens only when someone asks for one. No push, pull
request or tag runs a test. What is asked for by default is small: the **guards** of the modules a
change touched, about a tenth of each module's tests, chosen so that a failure among them is one a
person would otherwise meet. The whole suite is one switch away.

## Running the tests

Build first, then run the script the CI workflow also uses:

```powershell
dotnet build 0z0-shared.slnx -c Release
.\.github\scripts\run-tests.ps1                       # guards of the modules changed since origin/main
.\.github\scripts\run-tests.ps1 -Module mqtt, config  # guards of the modules named
.\.github\scripts\run-tests.ps1 -AllModules           # guards of every module
.\.github\scripts\run-tests.ps1 -Full                 # every test: the whole suite
.\.github\scripts\run-tests.ps1 -Full -Module update  # every test of the modules named
```

Module names are separated by commas. A space-separated list is refused rather than read as one
module, because the second name would otherwise be taken for another argument and the run would
cover the first module alone.

- **A module is a component key**, the first segment of a test project's name after `ZeroZero.`,
  lower-cased. `ZeroZero.Config.Tests`, `ZeroZero.Config.Sections.Tests` and
  `ZeroZero.Config.Watch.Tests` are all `config`.
- **With no module named, the modules are worked out from the change.** A path inside `src/<project>/`
  or `tests/<project>/` selects that project's module. Every other path — the workflows, the scripts,
  the build kit, the guides, the root files — selects `releaseverification`, whose tests read those
  files. Uncommitted work counts. `-Since` moves the comparison off `origin/main`.
- **The run fails on its own account** when a selected test project matched no guard at all, when a
  module name is not a component, and when nothing changed and nothing was named. `dotnet test`
  exits zero for a filter that matched nothing, so without the first check a project whose marks
  were lost would pass having run nothing.
- **In CI**, the workflow's manual trigger runs the script: *Actions → CI → Run workflow*. Leave
  *modules* empty for every module, or give component keys separated by spaces; tick *full* for every
  test instead of the guards. A push or pull request builds the solution and runs no test.
- **A release runs no test.** A tag is gated by its guards (the tag, the notes, the declared versions
  and the released dependencies), by the build, and by the verify job that fetches the published
  packages back and compares them with what the build packed. [`releasing.md`](releasing.md) has the
  whole order.

## What a default run does not prove

A green default run means the guards of the selected modules pass. It does not mean the suite passes,
and the gap is specific:

- **Modules that were not selected did not run.** Selection follows where a file lives, not who uses
  it. A change to `ZeroZero.Primitives` runs the primitives guards and not the MQTT tests that drive
  the coalescing gate under every retained channel; a change to `ZeroZero.Win32` runs nothing of
  the tray or the settings shell, which build on it.
- **Most tests in a selected module did not run.** Left out by design: extra rows of a parameterised
  test, wording checks beyond a few that carry a warning, and any path a guard already covers end to
  end. Some behaviours have no guard at all, among them the limit on how many quarantine copies the
  settings store keeps, the lifting of the sectioned store's refusal after a successful reload, the
  update component's sweep of stale downloads, each broker setting reaching the MQTT connection (one
  of ten is covered), another executable's crash dumps being left alone, and the guides the
  release-verification tests hold to the code — the adoption guide, the build guide, the consuming
  guide and the packaged readme.
- **A guard in `releaseverification` runs only when that module is selected.** The brand typeface
  test — a font URI in the brand markup that must name a file the package carries — lives there, so
  a change confined to the brand folder does not run it by default. Nor does a change confined to
  any component's folder run the checks on how that component packs.
- **Some guards skip where the machine cannot run them.** The tray host's throttling check needs the
  Windows App Runtime registered for the user and skips on a build runner. Tests marked for an
  elevated process skip from a standard token in every run, default or full; one of them is the only
  test that the account name the Task Scheduler reads back is recognised, and a miss there rewrites
  the watchdog task on every start.
- **A guard that loses its mark drops out without a sound.** The run refuses a project left with no
  guard at all; one guard fewer only lowers the count it prints.
- **A release proves what was packed, not how it behaves.** Nothing on a tag runs a test.

A full run closes every gap above except two: the skips remain in every run, and a release runs no
test at all.

## What earns the mark

A guard carries `[Trait(Guard.Category, Guard.Value)]`. The two constants live in `tests/Guard.cs`,
which `tests/Directory.Build.props` compiles into every test project, so a mistyped constant fails
the build. A mark written with string literals of its own has no such protection.

A test earns the mark when a failure there would reach a person, or when it pins a value that must
never move:

- **A regression witness** for a defect that shipped.
- **The only cover of a case every installation meets**, such as a fixture recording a real installed
  document.
- **A guard that stops something** — a refusal, a limit, a lock — where the code under it could
  plausibly break without anything else going red.
- **A pin** on a document, a wire format or a value that must not drift.

Not earned by another row of a theory, by a test whose failure always comes with a marked one, or by
a path a marked test already covers. A mark on a `[Theory]` marks every row, so a wide theory is an
expensive guard.

About a tenth of each module is the ceiling aimed at. A module keeps more where cutting to a tenth
would leave a distinct failure mode unwatched; the small modules do.

A new test project needs at least one guard, or a default run of its module fails.
`git grep -n "Guard.Value" tests/` lists every guard.
