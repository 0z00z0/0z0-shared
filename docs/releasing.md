# Releasing a component

Components release independently. One tag names one component, the workflow packs and pushes that
component's packages and nothing else, and its notes speak about that component alone.

## The component key

The key is the first segment of an assembly name after `ZeroZero.`, lower-cased: `ZeroZero.Mqtt`,
`ZeroZero.Mqtt.Discovery` and `ZeroZero.Mqtt.WinUI` are all `mqtt`. The key names the version
property (`MqttVersion` in `Versions.props`), the tag prefix (`mqtt-v0.7.0`), the notes folder
(`docs/release-notes/mqtt/`) and the guide (`docs/zerozero-mqtt.md`). The release scripts select
projects by it, so the rule is load-bearing, not cosmetic. A project that does not pack — the
interactive harness — is never part of a release, whatever its name.

**A new assembly's name decides which component it joins, and nothing checks that the join was
meant.** Whatever follows the first segment is invisible to the key, so an assembly named
`ZeroZero.Primitives.WinUI` would be `primitives`: released under the plain assembly's tag, at its
version, in its notes, and bumping the plain assembly's version with every change of its own. An
assembly that must version on its own takes a first segment no `<Key>Version` in `Versions.props`
declares yet — the visual controls assembly is `ZeroZero.Controls.WinUI`, key `controls`, for that
reason. Check the name against the declared keys before creating the project.

## What every component depends on

Packing turns a project reference on another component into a package dependency on that
component's **declared** version. Releasing a component therefore asserts that every component it
references is already on the feed at the version its `Versions.props` property currently holds, and
the workflow refuses the tag otherwise. A component with unreleased changes releases first, under
its own tag and its own notes.

The references that cross component lines today: all three MQTT projects, both diagnostics
projects, `ZeroZero.Lifecycle` and `ZeroZero.Startup` take `ZeroZero.Primitives`, `ZeroZero.Mqtt`
and `ZeroZero.Mqtt.Discovery` take `ZeroZero.Config`, `ZeroZero.Brand.WinUI`,
`ZeroZero.Controls.WinUI` and `ZeroZero.Tray` take `ZeroZero.Win32`, and `ZeroZero.Mqtt.WinUI`
takes `ZeroZero.Controls.WinUI`. So `primitives`, `config`, `win32` and `build` release in any
order, `brand`, `controls` and `tray` release after `win32`, `diagnostics`, `lifecycle` and
`startup` release after `primitives`, and `mqtt` releases after `primitives`, `config`, `win32`
and `controls`. No component references another component: the brand, diagnostics, lifecycle,
MQTT and startup components are independent of each other, and the build kit references nothing
and is referenced by nothing.
Within a component the order does not matter: the projects release together. The build kit packs
no assembly — its package is the MSBuild files, the manifest template and the signing script — and
the pack step counts it like any other project of its key.

## The procedure

1. **Bump the version.** Raise `<Key>Version` in `Versions.props`. Pre-1.0 per component: a
   consumer-visible break is a minor bump, anything else a patch. Every packable project of the
   component declares `<Version>$(<Key>Version)</Version>`, so the one property is the whole change.
2. **Write the notes** at `docs/release-notes/<key>/v<x.y.z>.md`: what the release contains and
   what it breaks, for a consumer of that component. The notes are the only place breaking changes
   are collected.
3. **Release what it depends on first**, if any referenced component has a version with no tag.
4. **Push the head**, then tag it `<key>-v<x.y.z>` and push the tag.

The workflow then runs, in this order, and stops at the first failure:

| Step | Refuses when |
|---|---|
| Tag names a component | The tag is not `<key>-v<x.y.z>` with the key in lower case, or `Versions.props` declares no `<Key>Version`. A bare `v0.7.0` and a `Mqtt-v0.7.0` both fail here. |
| Release notes present | `docs/release-notes/<key>/v<x.y.z>.md` is missing. |
| Tag matches the declared version | `<Key>Version` or the evaluated `Version` of any packable project of the key differs from the tag, or no project has the key. |
| Referenced components are released | A project reference to another component names a version whose tag `<other>-v<version>` is not on the remote. |
| Unit tests | Any test project fails — the whole suite runs, not the component's alone. |
| Pack the component | The output folder does not end up holding exactly one `<Name>.<version>.nupkg` per selected project. Once it does, the script writes `release-artefacts.json` beside the packages — the tag, the commit and the SHA-256 of every package as packed — and the job keeps that record as a workflow artefact. |
| Push to GitHub Packages | Any push fails, `409` for an already-published version included. |
| Verify release | What the feed serves is not what this run packed — the assertions below. A job of its own, so nothing the release job built is in reach and the only thing checked is what came down the wire. |
| Create the release | Runs only once verification has passed, so a tag whose packages did not verify has no release page announcing them. |

A green run means the component's packages are on the feed, are the ones this run packed, and a
GitHub release exists for the tag with the notes as its body. Nothing else is republished: a
release of `mqtt` leaves the `brand`, `build`, `config`, `controls`, `diagnostics`, `lifecycle`,
`primitives`, `startup`, `tray` and `win32` packages at whatever version each last released.

Tags from `v0.1.0` to `v0.6.0` predate the scheme and released every package together under one
number; they remain valid refs and their notes stay at `docs/release-notes/v<x.y.z>.md`.

## What verification asserts

Schema validation says a document is well formed; it says nothing about whether the thing it
describes is this build. `verify-release.ps1` takes the record the pack step wrote and the place
the artefacts were published, fetches every recorded artefact from there, and asserts:

- **Bytes.** The SHA-256 of what came down equals the hash recorded at pack time. This is the
  assertion that closes the loop. Two packs of one commit differ in SHA-256 every time (the
  package's own metadata part carries a fresh identifier and timestamp), so a feed holding a
  well-formed package at the right id, version, commit and stamp can still be another pack, and
  every other check passes on it.
- **Identity.** The nuspec's id and version are the artefact's own, and its repository commit is
  the commit being released.
- **Stamp.** Every assembly under `lib/` reports `<version>+<commit>` for the released version at
  that commit, so a package packed from a stale build is refused even when its nuspec is right,
  and an assembly with no stamp is refused rather than passed unread.
- **Manifest** (`-Manifest`). The manifest's version key equals the released version; its URL key
  contains the version and names a file this build produced; the URL fetches, and the bytes hash
  to the manifest's declared hash and to the build's. A manifest that is valid YAML, passes
  `winget validate`, points at a file that exists and matches that file's hash is still refused
  when the file is another build's.
- **Signed** (`-RequireSigned`). Evidence that signing happened is a step's recorded outcome,
  `steps.<id>.outcome`, never the job's colour. The step that signs must be `success`; or the
  warn-only step that says an installer is unsigned must be `skipped` — that step cannot fail a
  job, so its having run is the only record that the installer shipped unsigned, and its being
  skipped the only evidence that it did not.
- **Signer** (`-Signer`, `-SignerThumbprint`). Every executable fetched carries an intact
  Authenticode signature by the expected subject, and by the expected certificate where a
  thumbprint is given.

Every assertion fails closed: a location that cannot be reached, a record naming another tag or
commit, an empty record, a package without a nuspec — each is a failure, never a skip. The hash is
taken once, at pack time, and never recomputed from a rebuilt artefact: a hash taken again agrees
with itself and proves nothing.

What it deliberately does not check: a package with no assembly under `lib/` — the build kit — has
no stamp to read and passes on bytes and nuspec alone; a signature's chain, because the studio
certificate is self-signed and no runner trusts its root, so `-Signer` proves the subject string
and only `-SignerThumbprint` proves the certificate; and the build itself — the tests, the version
guards and the dependency guard run before packing and are not repeated after it.

## Verifying an application's release

The same check runs for an application from the reusable workflow, pinned at any component tag,
since a tag is a whole-tree snapshot. The application's build job writes a record to the same
shape — `tag`, `version`, `commit` and `artefacts` with a `name` and `sha256` each — beside any
manifest it rewrote, and uploads them as one workflow artefact:

```yaml
verify:
  needs: build
  uses: 0z00z0/0z0-shared/.github/workflows/verify-release.yml@primitives-v0.7.0
  with:
    tag: ${{ github.ref_name }}
    record-artifact: release-record-${{ github.ref_name }}
    location: https://github.com/<owner>/<repo>/releases/download/${{ github.ref_name }}
    manifest: .zerozero-release-record/manifests/<PackageIdentifier>.installer.yaml
    require-signed: true
    unsigned-outcome: ${{ needs.build.outputs.unsigned-outcome }}
    signer: CN=ZeroZero Software
    signer-thumbprint: <thumbprint>
```

- **The gate.** `0z00z0/0z0-shared/.github/actions/signing-gate@<tag>`, first in the build job
  with `secret: ${{ secrets.<NAME> }}`, fails a tagged run whose signing secret is absent, so a
  release cannot ship unsigned in silence; a branch run without the secret passes and says that
  nothing from it is a release. Only the secret's presence is read.
- **The outcome.** Give the warn-only unsigned-installer step an `id`, expose
  `${{ steps.<id>.outcome }}` as a job output, and pass it as `unsigned-outcome`; or pass the
  signing step's own outcome as `signing-outcome`. Both may be given, and each must hold.
- **The manifest.** Rewrite it through `manifest.ps1`: dot-source the script and call
  `Set-ManifestValue <path> <Key> <value>`. The key must match exactly one line — at any
  indentation, as a list item, never in a comment — and a key that matches nothing or twice
  throws naming the key, because a rewrite that matched nothing once left a template's values in
  place release after release with every step green. The file's line ending and byte-order mark
  are kept, and the value is read back from disk after the write.
- **The feed token.** A feed that authenticates reads takes a token through the `feed-token`
  secret; GitHub Packages takes the run's `GITHUB_TOKEN`, and public release assets need none.
  The token is sent only to `https` locations. The calling workflow's permissions apply to the
  verify job, so a feed on GitHub Packages needs `packages: read` there.

## Running the guards locally

The guards are scripts under `.github/scripts/`, so a release can be rehearsed before a tag exists:

```powershell
dotnet restore 0z0-shared.slnx
.\.github\scripts\release-guard.ps1 -Tag mqtt-v0.7.0 -Check Tag
.\.github\scripts\release-guard.ps1 -Tag mqtt-v0.7.0 -Check Notes
.\.github\scripts\release-guard.ps1 -Tag mqtt-v0.7.0 -Check Versions
.\.github\scripts\release-guard.ps1 -Tag mqtt-v0.7.0 -Check Dependencies
dotnet build 0z0-shared.slnx -c Release --no-restore
.\.github\scripts\run-tests.ps1
.\.github\scripts\pack-component.ps1 -Tag mqtt-v0.7.0 -Output artifacts
dotnet nuget push artifacts\ZeroZero.Mqtt.0.7.0.nupkg --source C:\feed
.\.github\scripts\verify-release.ps1 -Tag mqtt-v0.7.0 -Artefacts artifacts\release-artefacts.json -Commit (git rev-parse HEAD) -Location C:\feed
```

`Dependencies` asks the remote for its tags; `-Remote` points it at another repository, which is how
the check is exercised without a real tag. The last two lines publish to a folder feed and verify
against it, the way the tests do; against the real feed, `-Location` is
`https://nuget.pkg.github.com/0z00z0/download` with `-Layout NuGet`, and `ZEROZERO_FEED_TOKEN`
holds a token that can read packages. Each script exits non-zero on the condition it guards and
prints the reason in the form the workflow log shows. The tests under
`tests/ZeroZero.ReleaseVerification.Tests` drive the scripts through `pwsh` against a package the
repository's own packing produced, so a check that cannot fail is a test that fails.
