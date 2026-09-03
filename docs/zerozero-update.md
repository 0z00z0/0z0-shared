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
| Platform | Windows 10 1809 (build 10.0.17763) or later. The signature check is WinVerifyTrust. |
| Manifest | The install dialog is a task dialog, which needs common controls version 6 in the consuming application's own manifest; [`zerozero-win32.md`](zerozero-win32.md) carries the declaration. Without it the question is asked as a yes-or-no message box, and nothing else changes. |
| The release | A GitHub release, not a draft and not a pre-release, whose tag is a plain version (`v1.2.3`), which carries an asset named exactly as `InstallerFileName` says with the version substituted, and whose body carries the installer's SHA-256 as the only 64-digit hexadecimal token in it. |
| The installer | Authenticode-signed by the expected signer, per-user, and able to start while the application exits: the flow launches it without elevation and the application exits once it has started. |

## What it contains

`ZeroZero.Update`:

- **`UpdateOptions`** — everything the application supplies: the repository owner and name, the
  product name for the user agent, the running version (the entry assembly's when null), the
  expected signer, the download-directory prefix, the installer file name with `{version}` in it,
  the installer's arguments, the initial delay and the check interval, the request and download
  timeouts, the API base and the log sink. Validated when the service is built.
- **`ExpectedSigner`** — who must have signed the installer: the certificate subject, and the
  thumbprints (SHA-1 or SHA-256) of the certificates accepted when the machine does not trust the
  chain. `Match` says whether a certificate is that signer, and why not.
- **`UpdateService`** — `CheckAsync` finds the latest release and compares it with the running
  version; `PrepareAsync` downloads the installer into a fresh directory and verifies it, and never
  runs it; `Launch` verifies the prepared file again and starts it through the shell;
  `SweepStaleDownloads` removes download directories earlier runs left behind. One instance per
  application, owning its two HTTP clients for the life of the process.
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
  install now, not now, open the release page — with the stripped notes as its expandable detail,
  and message boxes for up to date, nothing released, a check that failed, an update that cannot
  be installed and a launch that failed. Every text names the application and says what has and
  has not run.
- **`UpdateFlow`** — `RunAsync(trigger)`: check, ask, prepare, launch, then call the application's
  shutdown. A manual run reports every outcome; a scheduled one speaks only when there is
  something to install and logs the rest. One run at a time: a scheduled check that finds a
  manual one on screen backs off.

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

The signer check has two forms, decided by what Windows says of the certificate chain. Under a chain
the machine trusts, the subject alone must match. Under a chain it does not trust — every machine on
which a self-signed studio certificate has not been installed — the subject must match **and** the
certificate must be one the application pins by thumbprint. A subject-only rule would accept any
self-signed certificate spelling the same name. The verdict says which form applied.

Verification runs twice: when the file has been downloaded, and again at the moment of launch, so
the bytes that were verified and the bytes that run are the same bytes or nothing runs. A refused
file is deleted, the verdict is logged, and the person is told that nothing has run and not to run
the file by hand.

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

A menu item or the About window's "check for updates" calls `flow.RunAsync(UpdateTrigger.Manual)`
from the same thread. `RunAsync` continues on the caller's context after each await, so the
prompts appear where the call was made; the scheduler's callback runs on a pool thread, and the
application marshals it to its dialog thread as the sketch shows.

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
  release before the rotation carries both thumbprints.
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
