# The build kit

`ZeroZero.Build` is the build machinery every repository in the family shares: the language and
studio-identity property blocks, the unpackaged WinUI application block, the application manifest
as a token-substituted template, the signing script with its publish-time target, and the
third-party pins under central package management. It is build-time only — the package carries no
assembly and nothing references it at compile time — and it comes with one standing rule: **every
repository in the family pins its packages centrally, through the kit's pin file**, so two
repositories cannot resolve two versions of the same package without a build failing.

The kit is versioned as `BuildVersion` in `Versions.props` and released under `build-v<x.y.z>`
tags, with notes under `docs/release-notes/build/`; [`releasing.md`](releasing.md) has the
procedure. It references no other component and no component references it, so it releases in any
order. This repository builds under the kit itself — `Directory.Build.props`,
`Directory.Build.targets` and `Directory.Packages.props` here import the same three files a consumer
imports — so a file that breaks breaks the solution build before it reaches a release.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows, for the WinUI block, the manifest and signing; the property blocks and the pins are portable. |
| Shell | PowerShell 7 (`pwsh`) on the path for the signing step; `ZeroZeroPowerShell` names another executable. |

## What it contains

| File | What it is |
|---|---|
| `Sdk/ZeroZero.Build.props` | The language settings (`LangVersion` latest, `Nullable` and `ImplicitUsings` enabled, `NeutralLanguage` en-GB) and the studio identity (`Authors`, `Company`, `Copyright`). Defaults: a value set in a project file wins. Also `ZeroZeroBuildDir`, the folder the kit's other files are reached from. |
| `Sdk/ZeroZero.Packages.props` | Central package management switched on, `VersionOverride` switched off, and the family's pins — the table under [Third-party pins in `consuming.md`](consuming.md#third-party-pins). |
| `Sdk/ZeroZero.WinUIApp.props` | The unpackaged WinUI application block, imported by an application project at the top of its own file: `OutputType` WinExe, the Windows target framework and minimum platform, `UseWinUI`, `WindowsPackageType` None, `EnableMsixTooling` false, `WinUISDKReferences` false, a `RuntimeIdentifier` from the process architecture, `DefaultLanguage` en-GB, and the manifest wiring below. |
| `Sdk/ZeroZero.Build.targets` | The guards, the manifest writer and the signing step. |
| `templates/app.manifest` | The application manifest with two tokens: `{AssemblyName}` and `{ExecutionLevel}`. It declares Windows 10 and 11 support, per-monitor-v2 DPI awareness and the common-controls-6 dependency the task dialog in `ZeroZero.Win32` needs. |
| `scripts/Sign-Executable.ps1` | Signs one or more files with a code-signing certificate and verifies what it signed. |
| `build/ZeroZero.Build.targets` | One target that fails the build of a project taking the kit as a `PackageReference`, because that route delivers nothing else of it. |

## The rule: one pin per package, across the family

Every repository has a `Directory.Packages.props` whose first line imports
`ZeroZero.Packages.props`, with the repository's own packages declared below it. The family's pins
then resolve to one version in every repository that imports the file, and a pin moves in that file
and nowhere else. A repository that needs a newer version of a family package raises it here, in
the kit, and takes the kit's next release.

Three guards make the rule mechanical rather than advisory: a project that has opted out of central
package management fails (`ZZB003`), a project whose `Directory.Packages.props` does not import the
family's pins fails (`ZZB002`), and a family pin that a later file has moved — by an `Update`, by a
second `PackageVersion` for the same name, at any version — fails (`ZZB006`). A `VersionOverride`
on a `PackageReference` is refused by NuGet itself (`NU1013`), because the pin file switches the
override off.

## Take the kit

One route per consuming repository, as for every component ([`consuming.md`](consuming.md)). On
either route the kit is three imports, each in the file the SDK already reserves for it. Nothing
else in the repository names the kit.

**Sibling-checkout route.** `ZeroZeroSharedDir` is the consumer's one line, set before the import
so the import can find the checkout; CI overrides it through the environment exactly as for a
project reference.

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <ZeroZeroSharedDir Condition="'$(ZeroZeroSharedDir)' == ''">$(MSBuildThisFileDirectory)..\0z0-shared</ZeroZeroSharedDir>
  </PropertyGroup>
  <Import Project="$(ZeroZeroSharedDir)\src\ZeroZero.Build\Sdk\ZeroZero.Build.props" />
</Project>

<!-- Directory.Build.targets -->
<Project>
  <Import Project="$(ZeroZeroSharedDir)\src\ZeroZero.Build\Sdk\ZeroZero.Build.targets" />
</Project>

<!-- Directory.Packages.props -->
<Project>
  <Import Project="$(ZeroZeroSharedDir)\src\ZeroZero.Build\Sdk\ZeroZero.Packages.props" />
  <ItemGroup>
    <!-- This repository's own packages, below the import. -->
  </ItemGroup>
</Project>
```

**Package route.** The kit is an MSBuild SDK, resolved at evaluation time from the feed the
repository's `nuget.config` names, at the version `global.json` names. The three imports are the
same files, reached by name through the `Sdk` attribute.

```json
{ "msbuild-sdks": { "ZeroZero.Build": "0.7.0" } }
```

```xml
<!-- Directory.Build.props -->
<Import Project="ZeroZero.Build.props" Sdk="ZeroZero.Build" />
<!-- Directory.Build.targets -->
<Import Project="ZeroZero.Build.targets" Sdk="ZeroZero.Build" />
<!-- Directory.Packages.props, above the repository's own PackageVersion items -->
<Import Project="ZeroZero.Packages.props" Sdk="ZeroZero.Build" />
```

**Never a `PackageReference`.** A `PackageReference` to the kit restores without complaint and
applies nothing: central package management is decided before restore runs, and a package's files
reach a project only after it. The kit's `build\` folder holds one target for that case, which fails
the build with `ZZB011` and names the route above. Nor can the kit be taken through the project's
`Sdk` attribute alone (`Sdk="Microsoft.NET.Sdk;ZeroZero.Build"`): NuGet enables central package
management only when a `Directory.Packages.props` was imported, so the pins have to arrive through
that file.

### An application project

An application imports the WinUI block at the top of its own file, before its own properties, and
names its execution level. Everything in the block is a default the project's own properties may
override, except `WindowsPackageType`, which the family keeps at `None` (`ZZB004`).

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="$(ZeroZeroBuildDir)Sdk\ZeroZero.WinUIApp.props" />

  <PropertyGroup>
    <AssemblyName>ChargeKeeper</AssemblyName>
    <ZeroZeroManifestExecutionLevel>requireAdministrator</ZeroZeroManifestExecutionLevel>
  </PropertyGroup>
  ...
</Project>
```

The manifest is written at build time to `obj\ZeroZero.Build\app.manifest` from the template, with
the assembly name and the execution level substituted, and embedded through `ApplicationManifest`.
The level defaults to `asInvoker`; elevation is a decision each application states. A project that
names its own `ApplicationManifest` keeps it, and nothing is written. `ZeroZeroManifestTemplate`
points the writer at another template; a token the kit does not know fails the build (`ZZB008`).

### Signing

The publish output is signed when a certificate is named, and nothing happens when none is.

| Property | Meaning |
|---|---|
| `ZeroZeroSignThumbprint` | A certificate in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`. |
| `ZeroZeroSignPfx` | A PFX file instead. Its password is `ZeroZeroSignPfxPassword`, which reaches the script as the environment variable `ZEROZERO_SIGN_PFX_PASSWORD` and never as an argument. |
| `ZeroZeroSignFile` | What to sign; defaults to `<PublishDir><AssemblyName>.exe`. |
| `ZeroZeroSignTimestampServer` | Another timestamp server than the script's default. `ZeroZeroSignNoTimestamp` true skips timestamping. |
| `ZeroZeroSignTrust` | Installs the certificate into the current user's Root and TrustedPublisher stores before signing, so a self-signed certificate verifies as Valid on a fresh runner. |

```powershell
$env:ZeroZeroSignPfxPassword = $env:SIGNING_PASSWORD
dotnet publish App\App.csproj -c Release -r win-x64 -p:ZeroZeroSignPfx=signing.pfx
```

The script signs SHA-256 with the full chain, reads the file back, and requires the signer to be
the certificate asked for and the status to be Valid — or, without `-Trust`, the untrusted-root
status a self-signed certificate yields on a machine that does not trust it, which it reports as
such. Any other outcome exits non-zero and fails the publish. An installer build script calls the
same file on the installer it produces, asking the project where the kit put it so the path holds
on either route:

```powershell
$script = dotnet msbuild App\App.csproj -getProperty:ZeroZeroSigningScript
pwsh $script -Path .\Output\App-Setup.exe -Thumbprint $thumbprint
```

### What stays in the application

Its version, product name, description and icons; its own content items; the model-pruning target
one application carries; the resource-index copy target and the ready-to-run and trimming settings,
which the kit does not carry. An application adopting the kit compares its inline block against
`ZeroZero.WinUIApp.props`, keeps in its own file whatever differs, and deletes the rest.

## The guards

Every guard is an error, never a warning, and names what to change.

| Code | Fails when |
|---|---|
| `ZZB001` | The targets are imported and the props are not. |
| `ZZB002` | The project does not see the family's pins: `Directory.Packages.props` lacks the import. |
| `ZZB003` | The project has opted out of central package management. |
| `ZZB004` | A project importing the WinUI block sets `WindowsPackageType` to anything but `None`. |
| `ZZB005` | `ZeroZeroManifestExecutionLevel` is not `asInvoker`, `highestAvailable` or `requireAdministrator`. |
| `ZZB006` | A family pin resolves at another version, or is declared more than once. |
| `ZZB007` | The manifest template does not exist. |
| `ZZB008` | The manifest template carries a token the kit does not know. |
| `ZZB009` | The file to sign is not there after publish. |
| `ZZB010` | The signing script does not exist. |
| `ZZB011` | The kit is taken as a `PackageReference`. |
| `ZZS001`–`ZZS012` | From the signing script: a malformed thumbprint, a certificate not found, a PFX missing, a password not set or wrong, a certificate that cannot sign or is not for code signing, a file that does not exist, a signature that fails to apply, and a signature that reads back absent, by another certificate, or with a status other than Valid or untrusted-root. |

## The source-revision stamp is not here

The commit stamp — `build/ZeroZero.Primitives.props` and `.targets` — belongs to
[`ZeroZero.Primitives`](zerozero-primitives.md), not to the kit. It is the build half of
`AssemblyVersionText.Read`, which reads the stamp back off the loaded assembly, so it ships with the
assembly that reads it and the one reference delivers a working version display. The kit neither
duplicates it nor imports it: an import would be a dependency between components that the release
guard cannot see, and packing it here would put an assembly's build files in a package that carries
no assembly. This repository's `Directory.Build.props` imports both, each from its own folder.

## Tests

The kit has no test project, because it has no assembly. It is exercised by every build of this
repository: the property blocks and the pins by every project, and the WinUI application block
with its manifest writer by the interactive harness, which imports the block the way a consuming
application does and carries no manifest of its own. Breaking any of the four guards behind the
block — `ZZB004`, `ZZB005`, `ZZB007`, `ZZB008` — fails the harness build. The signing step and
the package route are proved in a consumer outside the repository: a throwaway application and
test project in which the pins, the properties, the embedded manifest and the signature are read
back.
