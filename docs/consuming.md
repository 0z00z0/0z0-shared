# Consuming the library

How an application takes a component of this library: the two reference routes and what each
costs, making the reference resolve in CI, pinning, and the rules that hold on either route. This
document is component-neutral; each component's guide links here and adds only its own wiring.

## One reference per component, one route per repository

An application takes only the components it uses, and takes each through **one reference to the
component's entry point** — the project named in the component's guide. The entry point carries the
component's other projects transitively, so nothing else of this repository is referenced directly.

**One assembly is an exception, and it is an exception by design.** `ZeroZero.Config.Watch` is not
carried by the config component's entry point, because picking up an edit made outside the
application is a choice rather than a consequence of storing settings. A consumer that wants it adds
it as a second direct reference of the same component; a consumer that does not never sees it. It
releases under the same `config` tag as the rest, so the reference costs no extra version to track.

**Two routes, and both are supported.** A `PackageReference` on the studio's GitHub Packages feed, or
a `ProjectReference` on a sibling checkout of this repository. Neither replaces the other.

**A consuming repository uses one route for everything it takes, never both.** Two components taken
by different routes resolve a shared foundation assembly twice — once as a package dependency, once
as the sibling's project — and two same-identity assemblies from different sources is a conflict the
build may only warn about.

## The package route

Every assembly is published as a package of its own, the WinUI assemblies included. The packages
carry the compiled XAML and the `.pri` index beside the assembly, and the consuming build merges that
index into its own, so a consumer with no checkout of this repository anywhere builds, runs and
renders.

```xml
<ItemGroup>
  <PackageReference Include="ZeroZero.Brand.WinUI" Version="0.7.0" />
</ItemGroup>
```

The version is the component's tag without its prefix: `brand-v0.7.0` is `0.7.0`. Versions are per
component — see [Pin a tag](#pin-a-tag) — and a consumer with several components centralises its
`ZeroZero.*` versions in its own `Directory.Packages.props`, one line per component.

**What the pin buys, and it is the substantive difference:** a package version resolves the same
source locally and in CI. A sibling pin governs CI alone, so every local build, local test run and
locally built installer silently compiles whatever the sibling working tree currently holds, pin
file notwithstanding.

**What it costs: GitHub Packages authenticates every read, including of a public package.** An
anonymous request to the feed returns `401`. So the package route needs a token carrying
`read:packages` wherever a restore happens — on a developer's machine and on a CI runner alike —
where the sibling-checkout route clones a public repository with no credential at all. A consumer's
own workflow therefore gains a secret it did not previously need.

The consuming repository carries a `nuget.config` naming the feed and mapping which source may
answer for which package name:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="0z00z0" value="https://nuget.pkg.github.com/0z00z0/index.json" protocolVersion="3" />
  </packageSources>

  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="0z00z0">
      <package pattern="ZeroZero.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

**The mapping is not optional.** Without it restore asks every source for every package, so anyone
publishing a `ZeroZero.*` name to nuget.org could be resolved into a consuming app; none of these
names is reserved there. The mapping, not the absence of a source, is what keeps them on the studio
feed. Keep `nuget.org` in the list — everything else a consumer needs comes from there, and the
mapping is all-or-nothing: once it exists, a package matching no pattern fails to restore rather
than falling back.

**Never put a token in that file; it is tracked.** Register the credential against the *user-level*
`NuGet.Config` with `dotnet nuget add source`, passing the username and the token as the password.
On a runner, add the source in a step using a secret, or set `NUGET_AUTH_TOKEN` with
`actions/setup-dotnet`.

## The sibling-checkout route

A `ProjectReference` on a checkout of this repository, kept as a **sibling** of the consuming
repository's own checkout — `..\0z0-shared` from the consumer's project directory. Route it through
an MSBuild property that defaults to the sibling folder, so CI can point the same reference somewhere
else without editing the `.csproj`:

```xml
<PropertyGroup>
  <ZeroZeroSharedDir Condition="'$(ZeroZeroSharedDir)' == ''">..\0z0-shared</ZeroZeroSharedDir>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="$(ZeroZeroSharedDir)\src\ZeroZero.Brand.WinUI\ZeroZero.Brand.WinUI.csproj">
    <UndefineProperties>WindowsAppSDKSelfContained</UndefineProperties>
  </ProjectReference>
</ItemGroup>
```

`UndefineProperties="WindowsAppSDKSelfContained"` is required whenever the consuming app publishes
self-contained with that property set globally (on the command line, for instance): MSBuild
propagates a global property into every project reference, and the Windows App SDK targets reject
it on a class library — *"should not be applied to a class library"*. Stripping it for this
reference only lets the app aggregate the self-contained runtime while the library builds
framework-dependent. An app that instead declares `WindowsAppSDKSelfContained` as a project-level
property never propagates it and does not need the metadata.

The disk layout under `src/` is flat and stays flat: every project is `src/<Name>/<Name>.csproj`, so
the path above holds for any project in the repository.

## Make the reference resolve in CI

**On the package route there is nothing to fetch** — restore resolves the libraries like any other
dependency. What the workflow does need is a secret carrying `read:packages` and a step that adds
the source with it, because the feed refuses an anonymous read. That is the one thing the package
route adds to a consumer's CI, and the sibling-checkout route below needs no credential at all.

On the sibling-checkout route, a GitHub Actions runner checks out one repository, so the consumer's
workflow has to fetch this one as well. Two working shapes:

**Workspace subfolder + property override.** `actions/checkout` refuses a `path:` outside the
workspace, so the second checkout lands *inside* it and the `ZeroZeroSharedDir` property is
redirected there through job-level `env` (MSBuild reads environment variables as properties):

```yaml
jobs:
  build-test:
    runs-on: windows-latest
    env:
      ZeroZeroSharedDir: ${{ github.workspace }}/0z0-shared
    steps:
      - uses: actions/checkout@v7
      - uses: actions/checkout@v7
        with:
          repository: 0z00z0/0z0-shared
          path: 0z0-shared
```

The checkout then sits under the consuming project's own directory, where the SDK's default globs
would compile this repository's sources into the consuming assembly on top of the
`ProjectReference` — duplicate types and an ambiguous `NativeMethods`. Exclude the folder from the
consuming project's item globs:

```xml
<ItemGroup>
  <Compile               Remove="0z0-shared\**\*" />
  <Content               Remove="0z0-shared\**\*" />
  <None                  Remove="0z0-shared\**\*" />
  <Page                  Remove="0z0-shared\**\*" />
  <ApplicationDefinition Remove="0z0-shared\**\*" />
  <EmbeddedResource      Remove="0z0-shared\**\*" />
</ItemGroup>
```

That matches nothing locally, where the repository is an outside-the-tree sibling.

**Clone to a real sibling.** The alternative keeps the relative path identical to local development
by cloning (this repository is public, so no token is needed) beside the workspace checkout, and is
what a pinned build wants because `checkout --detach` takes an exact ref:

```yaml
      - name: Clone 0z0-shared (sibling dependency, pinned)
        shell: pwsh
        run: |
          git clone https://github.com/0z00z0/0z0-shared.git ../0z0-shared
          git -C ../0z0-shared checkout --detach $ref
```

## Pin a tag

**Every consumer-visible change is released under a component tag, `<key>-v<x.y.z>`** — `brand-v0.7.0`,
`mqtt-v0.7.0` — where the key is the first segment of the assembly name after `ZeroZero.`,
lower-cased. **A tag is the ref to pin, not a raw commit SHA.** It is also the package version, so the
same number pins either route. A tag reads as a version, so a pin bump is a legible diff and a
reviewable decision; a SHA says only that something moved. Each tag carries release notes under
`docs/release-notes/<key>/`, listing what changed in that component and nothing else, so **a
consumer raising a pin reads the notes for that tag first** — the breaking changes are stated there,
and there is no other place they are collected.

Tags from `v0.1.0` to `v0.6.0` carry no prefix: every package was released together under one
number, and a tag from that era remains a valid ref. From `0.7.0` every component carries its own
number.

The scheme is [semantic versioning](https://semver.org), per component, and every component is
**pre-1.0**: while the major stays `0`, a **minor** bump may break the API and a **patch** never does.
Tags are cut on a consumer-visible change, not on a calendar — the API, or the guidance a consumer
builds against, since a correction to the guides only reaches a consumer that pins tags when there
is a tag carrying it. Each component's version is declared once, as `<Key>Version` in
`Versions.props`, and every assembly of the component reports it; the release workflow refuses to
publish a tag that disagrees with it.

**On the package route** a consumer pins each adopted component and bumps a pin when that
component releases. A component's package depends on the foundation packages it takes at the
version declared when it was released; NuGet resolves such a dependency as a floor, so a consumer
may lift a foundation package directly without waiting for the component.

**On the sibling-checkout route** a consumer pins one git ref, and a tag is a whole-tree snapshot
regardless of its prefix. The rule: **pin the newest tag among the adopted components** — tags on
the default branch are totally ordered — and read the notes of every adopted component between the
old pin and the new. This is the one place the per-component scheme asks more of a consumer than a
single number did.

Both CI shapes above take a tag wherever they take a SHA. The **workspace subfolder** shape passes
it as the second checkout's `ref`:

```yaml
      - uses: actions/checkout@v7
        with:
          repository: 0z00z0/0z0-shared
          path: 0z0-shared
          ref: mqtt-v0.7.0
```

The **sibling clone** shape needs no change at all — a full `git clone` fetches tags, so
`checkout --detach $ref` resolves one. Shallow is the one thing to watch: `--depth 1` alone leaves
no tag to check out, so it comes with `--branch mqtt-v0.7.0`.

Local development builds against the live sibling checkout while CI builds the pinned tag, so a
consumer that adopts a newly added shared type builds green locally and fails CI with `CS0234`. A
consumer that wants reproducible builds therefore keeps two things: a **pinned-ref file** read by
every workflow that clones this repository (one file, so the pins cannot drift between CI and
release), and a **build-time drift guard** — an MSBuild target that compares the live sibling
checkout against that file and raises a warning, never an error, and skips entirely when either the
ref file or the sibling clone is absent. Put the tag in that file rather than the SHA it resolves
to, and let the guard resolve it — the pin is then readable where it is edited.

**The guard warns; it never redirects.** Nothing reads the ref file to decide which sources compile,
so a pin protects CI and nothing else: a local build, a local test run and an installer built on a
developer's machine all carry the sibling working tree. A consumer wanting the pinned revision
locally checks the sibling clone out at it — and builds against a clean sibling at a known revision,
never one mid-edit, since a tree with work in flight yields a partially applied change set that is
worse than either the old state or the new.

**A package pin does not have that gap.** A `PackageReference` version resolves the same assemblies
on a developer's machine as on a runner, because nothing about a restore consults the working tree.
That is the substantive reason to take [the package route](#the-package-route), and the price is the
token every restore then needs.

## Traps

- **`DefaultLanguage`.** An app that ships its own language-folder resources declares
  `<DefaultLanguage>en-GB</DefaultLanguage>` in its own project. A library declares it for its own
  `.resw`, but a merged app PRI is built from the app's resources and no library can declare the
  default on the app's behalf — without it MakePRI compares them against `en-US` and warns `PRI257`.
- **`UndefineProperties`** on the sibling route, above.
- **`PerMonitorV2`.** The consuming app's `app.manifest` declares per-monitor-v2 DPI awareness, so
  every shared window renders sharp on a high-DPI display rather than being bitmap-stretched.
- **Common controls 6.** The task dialog in `ZeroZero.Win32` exists in version 6 of the common
  controls only, and the consuming app's own manifest is the one place that decides which version
  its process loads; [`zerozero-win32.md`](zerozero-win32.md) carries the declaration. Without it
  `NativeTaskDialog.IsAvailable` is false and `Show` throws, naming the dependency; the update
  component's install question falls back to a yes-or-no message box.

## The build kit

Every repository in the family also takes the [build kit](zerozero-build.md), on the same route as
its components: three imports, in `Directory.Build.props`, `Directory.Build.targets` and
`Directory.Packages.props`. It carries the shared property blocks, the WinUI application block, the
manifest template, signing, and the pins below — and the rule that every repository pins through
it, which its guards enforce.

## Third-party pins

Every version is declared once, in the build kit's `ZeroZero.Packages.props`, which this
repository's `Directory.Packages.props` and every consuming repository's import. A consumer that
imports the file resolves exactly these versions: an `Update`, a second `PackageVersion` or a
`VersionOverride` fails the build (`ZZB006`, `NU1013`), and a pin moves in the kit and nowhere
else. The pins do not all behave the same way, and the difference matters to a consumer that has
not yet taken the kit:

| Package | Version | What the pin is |
|---|---|---|
| `Microsoft.WindowsAppSDK` | `2.2.0` | A **floor**. |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.28000.2270` | A **floor**. |
| `CommunityToolkit.WinUI.Controls.SettingsControls` | `8.2.251219` | A **ceiling**. |
| `H.NotifyIcon.WinUI` | `2.4.1` | A **ceiling**, for the same reason: the tray host's notify-icon library, which a consuming application references directly as well. No type of it reaches a public signature. |
| `MQTTnet` | `5.2.0.1603` | Transitive only, and no type of it reaches a public signature. |
| `TaskScheduler` | `2.12.2` | Transitive only, through `ZeroZero.Startup`; no type of it reaches a public signature. |
| `Microsoft.Win32.SystemEvents` | `10.0.11` | Transitive only, through `ZeroZero.Lifecycle` and `ZeroZero.Tray.WinUI`; no type of it reaches a public signature. The runtime's own assembly, pinned at the .NET 10 servicing release the family builds on and raised with it. |
| `Microsoft.NET.Test.Sdk` | `18.8.1` | The test trio, for a consumer's own test projects. |
| `xunit` | `2.9.3` | |
| `xunit.runner.visualstudio` | `3.1.5` | |
| `Microsoft.Extensions.TimeProvider.Testing` | `10.9.0` | Test-only, and taken only by a suite that moves a clock rather than waiting on one. Nothing under `src/` references it, so it reaches no consumer that does not ask for it. |

**The two Windows App SDK pins are a floor, not a lock.** A consuming app that pins on its own may
pin higher, and NuGet unifies a package graph on the version nearest the consuming project, so the
app's own pin governs the Windows App SDK runtime that is actually resolved for the whole build.
Under the kit both sides carry the same number by construction, and a newer runtime is taken by
raising the pin in the kit.

**The Community Toolkit pin is a ceiling and behaves the opposite way.** A consuming app holding a
direct reference *below* this version fails to restore with **NU1605**, an error rather than a
warning, because a direct reference below a transitive one is a downgrade. Raise both or neither.
The notify-icon library's pin is a ceiling for the same reason: an application that adopts the
tray host and keeps a direct reference of its own resolves the kit's version or fails to restore.

**MQTTnet reaches a consumer transitively, through `ZeroZero.Mqtt`, and nothing requires the
consumer to reference it.** The module's own `MqttQos`, `MqttMessage`, `MqttConnackCode` and
`MqttPubackCode` stand in front of it, so an app never names an MQTTnet type and a version bump here
is not a consumer-visible API change.

No third-party type reaches a public signature of any assembly in this repository, so a bump of any
pin above is never an API change for a consumer.
