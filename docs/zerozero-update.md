# The update component

`ZeroZero.Update.Win32` is the entry point: the update dialogs, worded here and marshalled by the
Win32 foundation, and the check-ask-download-verify-launch orchestration that hands over to the
application's own shutdown. It carries `ZeroZero.Update`, the flow without its dialogs: the latest
GitHub release against the running version, the download into a fresh private directory, the
verification of the installer before it runs, the launch-or-refuse policy, the stale-download sweep
and the check scheduler. Both are plain `net10.0` and declare themselves Windows-only.
`ZeroZero.Update` takes `ZeroZero.Primitives` for the log sink and the version reader;
`ZeroZero.Update.Win32` takes `ZeroZero.Win32` for the task dialog and the message boxes. No
package reference in either.

The assemblies are versioned as `UpdateVersion` in `Versions.props` and released under
`update-v<x.y.z>` tags, with notes under `docs/release-notes/update/`;
[`releasing.md`](releasing.md) has the procedure. The component releases after `primitives` and
`win32` are on the feed at the versions it references.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows. Both assemblies target plain `net10.0` and declare themselves Windows-only through `SupportedOSPlatform`, with no version: nothing here needs a build floor, and neither project states one. The signature check is WinVerifyTrust. An application taking the component alongside the WinUI components inherits their floor, not one from here. |
| Manifest | The install dialog is a task dialog, which needs common controls version 6 in the consuming application's own manifest; [`zerozero-win32.md`](zerozero-win32.md) carries the declaration. Without it the question is asked as a yes-or-no message box, which costs two things: the release notes, which the task dialog shows as its detail, and the third choice — "Open the release page" — so a person offered the message box can install or defer and cannot read the notes in a browser first. |
| The release | A GitHub release, not a draft and not a pre-release, whose tag is a plain version (`v1.2.3`), which carries an asset named exactly as `InstallerFileName` says with the version substituted, and whose body carries the installer's SHA-256 as the only *distinct* 64-digit hexadecimal token in it — one value, repeated as often as the notes like. |
| The installer | Authenticode-signed by the expected signer, per-user, and able to start while the application exits: the flow launches it without elevation and the application exits once it has started. |

## What it contains

`ZeroZero.Update`:

- **`UpdateOptions`** — everything the application supplies: the repository owner and name, the
  product name for the user agent, the running version (the entry assembly's when null), the
  expected signer, the download-directory prefix, the installer file name with `{version}` in it,
  the installer's arguments, the initial delay and the check interval, the request and download
  timeouts, the API base and the log sink. Validated when the service is built.
- **`ExpectedSigner`** — who must have signed the installer: the certificate subject, the
  thumbprints (SHA-1 or SHA-256) of the certificates accepted when the machine does not trust the
  chain, and `acceptSelfSignedSubject`, which lets the publisher name alone carry an untrusted
  chain. `Match` says whether a certificate is that signer, and why not.
- **`UpdateService`** — `CheckAsync` finds the latest release and compares it with the running
  version; `PrepareAsync` downloads the installer into a fresh directory and verifies it, and never
  runs it, taking an optional reporter the download's progress goes to; `Launch` verifies the
  prepared file again and starts it through the shell; `SweepStaleDownloads` removes download
  directories earlier runs left behind. One instance per application, owning its two HTTP clients
  for the life of the process.
- **`DownloadProgress`** — bytes received and the total where there is one, the pair a progress bar
  is drawn from. [Below](#reporting-the-download) has how often it arrives and what it does not
  report.
- **`UpdateCheckOutcome`** — what a check found, and the whole of what an application needs to
  decide what to say. `UpdateAvailable` and `UpToDate` are the two answers a release gives;
  `NoReleases` is the repository having published none; and five say the check did not get one:
  `RateLimited`, `Unreachable` (nothing answered), `TimedOut` (something is there and did not answer
  in time), `RequestFailed` (a failure status rather than a release) and `InvalidResponse` (an answer
  that is not a release this version understands). Every one carries a `Detail` sentence for a person
  to read; **none of them has to be read to tell the outcomes apart**, which is the point of there
  being eight. A cancellation the caller asked for is no outcome at all: the check throws.
- **`UpdateScheduler`** — runs a check after an initial delay and then at an interval, one at a
  time, counted from process start and never persisted: the component stores nothing.
- **`InstallerVerifier`** — the two checks below, as one call with one verdict.
- **`PublishedHash`** — the installer's SHA-256 read from the release body; `ReleaseNotesText`
  strips the notes for a dialog and leaves the hash line out; `VersionTag` reads a tag as a
  four-part version so a running `1.2.3` is not out of date against its own `v1.2.3`.
- **`GitHubReleaseSource`**, **`InstallerDownloader`**, **`DownloadDirectory`** and
  **`ShellInstallerLauncher`** — the pieces behind the service, each replaceable through an
  interface in a test.

`ZeroZero.Update.Win32`:

- **`NativeUpdatePrompts`** — the install question as a task dialog with three command links —
  install now, not now, open the release page — with the release notes as its expandable detail,
  and message boxes for up to date, nothing released, a check that failed, an update that cannot
  be installed and a launch that failed. The expander's text is the release body with its markdown
  stripped, unless the application supplies its own through the `releaseNotes` argument. Every
  refusal has a sentence of its own and ends the same way: the file was not run, and the release
  page is where to go instead.
- **`UpdateFlow`** — `RunAsync(trigger)`: check, ask, prepare, launch, then call the application's
  shutdown, returning an `UpdateFlowRun` carrying the result, the check it read and the release
  where there is one. A manual run reports every outcome; a scheduled one speaks only when there is
  something to install and logs the rest; a silent one shows nothing at all, a release included,
  and hands the release back for the caller's own surface. `InstallAsync(release)` starts an update
  from a release already found, without checking again. One install at a time, and one check at a
  time: a caller arriving while a check is in flight joins it and reads its result.
  `UpdateFlowOptions.Progress` is where the download's progress goes for a host driving the flow.

## Verification before execution

The downloaded file runs only after two checks, and each answers a question the other cannot:

1. **The SHA-256 against the hash the release publishes** — whether the download is whole. A
   truncated, corrupted or wrong file hashes differently and is refused before its signature is
   looked at.
2. **The Authenticode signature and its publisher against the expected signer** — whether the file
   is the publisher's. A file substituted for the real one, on the server or on the wire, fails
   here even when it carries a valid signature of its own, because the signer is not the one
   expected. A signed file altered after signing fails here too.

**A checksum published beside the file is not the second check.** Whatever can replace the file
can replace a hash sitting next to it, so a matching hash says only that the bytes that arrived are
the bytes that were published — nothing about who published them. Only the signature answers that,
which is why neither check can be turned off and the expected signer is the one input.

The signer check has three forms, decided by what Windows says of the certificate chain and by what
the application accepts. Under a chain the machine trusts, the subject alone must match. Under a
chain it does not trust — every machine on which a self-signed studio certificate has not been
installed — the subject must match **and** the certificate must be one the application pins by
thumbprint. The third form is the opt-in below. The verdict says which form applied.

The name is compared whole, without case, with both sides rendered through the same decoder, so
spacing and case differences between the application's string and the certificate's do not count
and a shorter name is never a match for a longer one.

### Trusting the publisher name alone

`new ExpectedSigner(subject, acceptSelfSignedSubject: true)` accepts an intact signature by the
expected subject under a chain the machine does not trust, with no pin. It is chosen per
application and off by default, so an application that does not ask for it keeps the pin
requirement exactly as it was.

What it tolerates is an untrusted root and nothing else. Every other fault the signature check
reports stops before the signer is looked at — a file altered after signing, a file with no
signature, an expired certificate, a chain that cannot be built — whatever this setting says. The
subject is compared before the untrusted chain is tolerated, so another publisher's self-signed
certificate never reaches it.

**What it knowingly accepts is a look-alike.** Anyone can mint a self-signed certificate spelling
the same name, and under this setting a file signed by one verifies. Pinning the certificate closes
that and breaks every certificate renewal; the name is the trade chosen. An application that can
carry a pin should carry one.

Revocation is a separate matter and always has been: the check asks Windows for no revocation
lookup, so a revoked certificate is refused only where Windows reports it from what it already
holds.

Verification runs twice: when the file has been downloaded, and again at the moment of launch, so
the bytes that were verified and the bytes that run are the same bytes or nothing runs. A refused
file is not run, the verdict is logged, and removing the file is attempted afterwards and separate
from the decision — it can fail, and its failure is logged rather than shown, which is why no
message claims the file was removed.

## Where the published hash comes from

The hash is read from the **release body** — the notes text the releases API returns with the
release. The body is part of the release JSON the check has already fetched, so the hash is
reachable exactly when the release is, with no second request, no separate asset and no
credential; a release whose body the updater can read is a release whose hash it can read. The
release workflow that attaches the installer writes one line in the body carrying the installer's
SHA-256, in upper or lower case, and the flow takes it.

The rule is strict: **exactly one distinct 64-digit hexadecimal token in the body**. A body with
none is `HashNotPublished` and nothing is downloaded — a file that cannot be verified is not
fetched, and the person is told the release publishes no hash. A body with two different tokens is
`HashAmbiguous` and nothing is downloaded either: which one is the installer's would be a guess. A
release that ships a second artefact publishes that artefact's hash somewhere other than the body.

Two routes were measured and not taken. A package-manager manifest attached as an asset carries
the installer's hash in its own format, but not every release of the family attaches one, and a
second download to read a hash that the first response already carries is a second thing that can
fail. A `.sha256` file beside the installer is the checksum the caveat above is about, and no
release of the family carries one.

## Wiring

Once, at start-up, on the thread that owns the dialogs:

```csharp
var options = new UpdateOptions
{
    RepositoryOwner = "studio",
    RepositoryName = "product",
    ProductName = "Product",
    ExpectedSigner = new ExpectedSigner("CN=Studio, O=Studio, C=NO", ["<SHA-256 thumbprint>"]),
    DirectoryPrefix = "Product-update",
    InstallerFileName = "Product-Setup-{version}.exe",
    Log = log,
};
var service = new UpdateService(options);
service.SweepStaleDownloads(TimeSpan.FromDays(1));

var prompts = new NativeUpdatePrompts(ownerWindowHandle, "Product", topmost: true);
var flow = new UpdateFlow(service, prompts, new UpdateFlowOptions
{
    Shutdown = () =>
    {
        lifecycle.MarkDeliberateExit();
        Exit();
    },
    Log = log,
});

var scheduler = new UpdateScheduler(options.InitialDelay, options.CheckInterval,
    token => RunOnTheDialogThread(() => flow.RunAsync(UpdateTrigger.Scheduled, token)), log);
scheduler.Start();
```

Which trigger a surface uses decides what reaches the screen:

| Trigger | What appears |
|---|---|
| `Manual` | Every outcome. For a surface with nothing of its own to report on — a tray menu item. |
| `Scheduled` | Nothing, except the install question when a release is found. |
| `Silent` | Nothing at all, a release included. The run carries the result and the release, and the caller reports on its own button. |

A silent check that found something installs from the release it was handed:

```csharp
UpdateFlowRun run = await flow.RunAsync(UpdateTrigger.Silent);
if (run.Result == UpdateFlowResult.UpdateAvailable) ShowTheButton(run.Release!);
// later, when the button is pressed:
await flow.InstallAsync(release);
```

`RunAsync` continues on the caller's context after each await, so the prompts appear where the call
was made; the scheduler's callback runs on a pool thread, and the application marshals it to its
dialog thread as the sketch shows.

**A caller arriving while a check is running joins it.** It is handed that same check and reads its
result, rather than starting a second request or being refused, so an About window opening during
the scheduled check costs nothing and cannot disagree with it. The shared slot is cleared as the
check ends, before any caller resumes, so a request arriving after that starts a fresh check. Each
surface shows its own waiting state while it waits; the component shows none. The caller that
started the check is the one whose cancellation token is inside the request.

## Reporting the download

A 64 MB installer takes long enough that an application showing nothing looks stopped. The
component measures the download and hands the measurements out; **it draws nothing of its own**,
for any trigger, so what appears on screen — a bar, a percentage, a line of text, nothing at all —
is the application's decision and lives on the application's own surface.

Each report is a `DownloadProgress`: the bytes received so far, and the total where there is one.

| | |
|---|---|
| How often | At most one report every 250 ms while bytes are arriving, counted from the last report sent, whatever the file's size or the line's speed. `InstallerDownloader.ProgressInterval` is the value. |
| The last report | A download that completes always ends with one report carrying its true final byte count, whether or not the interval was due. A bar that stops a little short of its end reads as a download that stalled. |
| A failure or a cancellation | Nothing is reported after the last interval report. That report is the last real measurement and nothing follows it claiming completion or resetting to zero. |
| An unknown total | `TotalBytes` is null and `Fraction` with it. Nothing is guessed, so a surface with no total shows a marquee or a byte count rather than a percentage. |
| No reporter attached | The download is the download it was before the interval existed: no clock is read and nothing is allocated for it. |

The total is the response's `Content-Length` where the server sends one, and the size the release
declares where it does not. It is null only when neither is available.

A host driving the flow attaches its reporter once, in the options:

```csharp
var flow = new UpdateFlow(service, prompts, new UpdateFlowOptions
{
    Shutdown = …,
    Progress = new Progress<DownloadProgress>(p => ShowBar(p.BytesReceived, p.TotalBytes)),
    Log = log,
});
```

A host driving the service directly passes one per call instead —
`service.PrepareAsync(release, progress, token)` — and the two layers report identically, because
the flow does nothing but hand the reporter down.

**Which thread a report arrives on is the reporter's business.** A `Progress<T>` constructed on the
thread that owns the windows posts back to that thread and is the easy choice; a reporter written
by hand is called on whatever thread the download is running on and marshals itself. The download
does not wait for a report to be handled.

## What stays with the application

- **The options.** Every product string, the repository, the installer file name and the expected
  signer with its pins. The component carries none of its own.
- **The running version**, when the entry assembly is not the application — a plug-in host, a
  test — through `UpdateOptions.RunningVersion`.
- **The owner window handle and the application name** the dialogs take.
- **The shutdown callback.** The flow calls it once the installer process exists and never before;
  when and how the application exits is its own decision. An application armed with the lifecycle
  component marks the exit deliberate first, or the relaunch hook starts it again under the
  installer.
- **Where the check is offered** — a menu item, the About window, both — and the thread the
  dialogs live on.
- **What a silent check shows.** The component shows nothing for that trigger, so the button, its
  waiting state, its label when a release is found and what it does with a failure are the
  application's.
- **What the download looks like.** The component measures it and reports the numbers; the bar,
  the wording, the thread it is drawn on and whether anything is shown at all are the
  application's.
- **The release-notes text**, where the application keeps its own rather than the release body.
- **The installer itself**: where it puts things, per-user or per-machine, elevation, and the
  step that closes a running application. The flow assumes a per-user installer that needs no
  elevation and an application that exits once the installer has started.
- **The release workflow** that signs the installer, attaches it under the expected name and
  writes its SHA-256 in the release body.

## Traps

- **The running version is the entry assembly's.** `Assembly.GetEntryAssembly()`, never the
  executing one: the executing assembly is this library once the code is shared, and its version
  would silently stand in for the application's. A host with no entry assembly, or one whose
  version is not the product's, sets `RunningVersion`.
- **Pin the next certificate one release ahead.** A certificate rotated in with the same release
  that first expects it is refused by every installed version, since none of them pins it. The
  release before the rotation carries both thumbprints. An application that accepts the publisher
  name alone has no rotation problem and no protection from a look-alike either; that is the trade,
  and it is made once, in the options.
- **A refusal says nothing about the file being gone.** Removing it is attempted after the
  decision, its failures are logged rather than shown, and a message claiming a deletion would be
  false in front of a person often enough to matter. Removal is also not all-or-nothing: the
  recursive removal of a download directory carries on past an entry it cannot take, so a failure
  can still have taken the installer with it.
- **A silent check that finds a release installs nothing.** It returns `UpdateAvailable` and the
  release; the install starts from `InstallAsync` when the person asks for it.
- **Progress stops where the download does, and the install does not.** Verification runs after the
  last byte arrives and reports nothing, so a bar that has reached its end sits full while the hash
  and the signature are checked. A surface that treats the final report as "finished" says so too
  early; the flow's own result is what finished means.
- **Attaching no reporter is the whole of turning progress off.** There is no switch, because there
  is nothing to switch: an application that supplies none is measured no differently from one on
  0.9.0.
- **One hash in the body, the installer's.** A second distinct hash anywhere in the notes — a
  portable build's, a checksum of a checksum — makes the release un-installable through the flow.
- **The tag is a plain version.** `v1.2.3` or `1.2.3`; a pre-release suffix, a component-prefixed
  tag or a name is an invalid response, never a release.
- **The asset name must match exactly.** The first executable in the release is never taken; the
  release must carry an asset named as `InstallerFileName` says, case included.
- **The rate limit is a state, not a failure.** An anonymous request is one of sixty an hour per
  address; a manual check refused by it is told when the limit lifts, and a scheduled one logs and
  waits for the next interval.
- **Nothing is persisted**, so the interval counts from process start: an application restarted
  every hour checks every hour.
- **The asset name needs the version placeholder.** Matching is exact, case-sensitive and has no
  wildcard, so an `InstallerFileName` written without `{version}` in it searches for the same
  literal name at every release and never matches an asset whose own name carries the version.
- **A release source built outside the component needs its own user agent.** GitHub refuses a
  request that carries none; only the client the component builds sets one, so a replacement
  `IReleaseSource` supplies its own.
- **There is no path that skips the published hash.** The verifier requires one to be present in
  the release body; a release that publishes none is refused before its signature is even looked
  at.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Update.Win32`; it brings
`ZeroZero.Update`, `ZeroZero.Primitives` and `ZeroZero.Win32` with it. A headless tool takes
`ZeroZero.Update` alone and supplies its own prompts.

The tests are in `tests/ZeroZero.Update.Tests` and `tests/ZeroZero.Update.Win32.Tests`, plain
`net10.0`, and run on Windows only. The release server is a loopback listener that writes exactly
the status, headers and bytes each test says, so a download that ends early is a socket closing
early. The verifier is exercised against real files: copies of the assembly under test signed
through PowerShell's `Set-AuthenticodeSignature` by certificates made in the test — the expected
signer, a stranger, and an impostor spelling the expected name with a key of its own — plus the
unsigned, tampered and truncated forms; the trusted-chain form runs against the runtime's own core
library where the machine trusts its signature, and is reported as skipped where it does not. The
launcher in the tests records and starts nothing, and the dialogs are read back as requests rather
than shown. Nothing reaches the internet, no installer runs, and no dialog appears on screen.

Status (2026-09-20): 0.10.0's reporting interval and its final report are proved by having been run
once, against a local server, rather than by tests of their own — the report count, their order, the
final count against the file's real length, a download with no reporter behaving as before, a report
reaching a host through the flow rather than the service, and a cancelled download sending nothing
after its last report. The two existing tests that already asserted progress still hold, because
they assert the reports are monotonic and that the last carries the whole length, neither of which
the interval changes.

What 0.9.0 added — the publisher-name-alone mode, the silent trigger, joining a
check already in flight, installing a release already found, and the host's own release-notes text —
is proved by having been run once rather than by tests of its own, and the suite covers it only
where an existing test already asserted the behaviour it replaced. One case is not proved at all: a
chain fault other than an untrusted chain, because Windows will not apply a signature with an
expired certificate and producing one would mean putting a certificate in a store. What was measured
instead is that a file altered after signing and an unsigned file both still refuse with the mode on,
and that the verifier reaches the signer check for one trust code only.
