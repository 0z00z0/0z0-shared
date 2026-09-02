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

## What every component depends on

Packing turns a project reference on another component into a package dependency on that
component's **declared** version. Releasing a component therefore asserts that every component it
references is already on the feed at the version its `Versions.props` property currently holds, and
the workflow refuses the tag otherwise. A component with unreleased changes releases first, under
its own tag and its own notes.

The references that cross component lines today: all three MQTT projects take
`ZeroZero.Primitives`, `ZeroZero.Mqtt` and `ZeroZero.Mqtt.Discovery` take `ZeroZero.Config`,
`ZeroZero.Brand.WinUI` takes `ZeroZero.Win32`, and `ZeroZero.Mqtt.WinUI` takes
`ZeroZero.Brand.WinUI`. So `primitives`, `config` and `win32` release in any order, `brand`
releases after `win32`, and `mqtt` releases after `primitives`, `config` and `brand` — last of
all. Within a component the order does not matter: the projects release together.

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
| Pack the component | The output folder does not end up holding exactly one `<Name>.<version>.nupkg` per selected project. |
| Push to GitHub Packages | Any push fails, `409` for an already-published version included. |

A green run means the component's packages are on the feed and a GitHub release exists for the tag
with the notes as its body. Nothing else is republished: a release of `mqtt` leaves the `brand` and
`config` packages at whatever version they last released.

Tags from `v0.1.0` to `v0.6.0` predate the scheme and released every package together under one
number; they remain valid refs and their notes stay at `docs/release-notes/v<x.y.z>.md`.

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
```

`Dependencies` asks the remote for its tags; `-Remote` points it at another repository, which is how
the check is exercised without a real tag. Each script exits non-zero on the condition it guards and
prints the reason in the form the workflow log shows.
