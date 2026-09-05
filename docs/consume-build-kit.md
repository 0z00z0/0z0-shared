# Replacing an application's own signing call and manifest

A checklist for an application that already signs its own output and already ships a hand-written
`app.manifest`, and wants the shared kit to carry both. The kit's contents, its import lines and its
guards are in [`zerozero-build.md`](zerozero-build.md); this says what changes hands and what does
not.

Take the kit before any component. Taken afterwards, its pins arrive as version conflicts rather
than as rules.

## What the signing step does not do

- **It signs one file.** The publish-time target signs the published executable and nothing else in
  the publish folder. The installer is signed by an explicit call to the same script from the
  application's own build script.
- **It does not publish the hash the update flow reads.** The SHA-256 in a release body is the
  application's release workflow's, and stays there.
- **It signs through PowerShell's own Authenticode call, not `signtool`.** Anything only that tool
  offers — appending a second signature, page hashes, naming a hardware-token provider on the
  command line — is out of reach.
- **One timestamp server, named by a property.** No fallback list, and no retry when it cannot be
  reached.
- **The certificate is a thumbprint in a personal store, or a PFX file.** The PFX password arrives
  only through an environment variable; nothing reads it from an argument and nothing prompts for it.

## What the manifest template does not declare

The template carries four things: Windows 10 and 11 support, per-monitor-v2 DPI awareness, the
common-controls-6 dependency the task dialog needs, and the requested execution level. Nothing else
— no long-path awareness, no UTF-8 active code page, no heap selection — and `uiAccess` is false.

**The execution level stays the application's.** It is the one value in the template a project sets,
it defaults to running as the invoker, and an application that registers its logon task from within
itself declares `requireAdministrator`. Only three values are accepted; anything else fails the
build.

An application whose existing manifest declares more than the template does keeps that manifest by
naming it: a project that names its own `ApplicationManifest` gets nothing generated.

Version, product name, description, icons, content items, and the ready-to-run and trimming settings
all stay with the application; the kit carries none of them.

## The checklist

1. Add the kit's three imports, and the fourth in the application project. The lines for both routes
   are in [`zerozero-build.md`](zerozero-build.md#take-the-kit).
2. Compare the application's inline WinUI property block against the kit's application block. Keep
   whatever differs in the project file — the project's own properties win — and delete the rest.
3. Move every third-party version into the kit's pin file, or confirm the kit already pins it at the
   version in use. A version attribute on a reference, a second declaration of a pinned package, and
   an override each fail the build.
4. Compare the hand-written manifest against the template line by line before deleting it. Anything
   it declares that the template does not is lost by the swap, and the swap is silent.
5. Set the execution level, then delete the manifest and let the kit write one — or keep it under
   `ApplicationManifest` and let nothing be written.
6. Replace the direct call to the signing tool with the kit's signing properties, and leave the
   release workflow's hash publication where it is.
7. Sign the installer through the same script, asking the project where the kit put it so the path
   holds on either route.

**Verify by reading the shipped file back**, not the build log: the signature off the published
executable, and the manifest out of the executable that was built, with the common-controls
dependency and the DPI setting in it.

## Traps

### A process-wide self-contained property fails every library in the graph

An application publishing self-contained with `WindowsAppSDKSelfContained` set as a **global**
property — on the command line, for instance — fails the build of every library it references, with
*"should not be applied to a class library"*. MSBuild propagates a global property into every
project reference, and the refusal fires on any project whose output is a library.

**It is not this repository's, and nothing here can remove it.** The refusing text exists only inside
Microsoft's own `Microsoft.WindowsAppSDK.Base` package, and sits in that package's transitive build
folder as well as its direct one — which is why a library that never references the Windows App SDK
itself still fails once the property reaches it. Five library components here take that package, so
the reach is the whole reference graph.

Two fixes, and neither of them is this repository's to make:

- **Strip the property on the referencing edge** — `<UndefineProperties>` naming it, on each project
  reference into the shared checkout. The application still aggregates a self-contained runtime while
  the libraries build framework-dependent. The shape is in
  [`consuming.md`](consuming.md#the-sibling-checkout-route).
- **Declare it as a project-level property in the application** rather than globally. A property set
  in a project file propagates to nothing.

A third switch in the same package turns the check off, and is not a fix: the self-contained targets
are still imported into the library, so only the message goes away.

### A missing pin file hides behind a generic NuGet error first

Deleting or misplacing `Directory.Packages.props` on a project whose `PackageReference` items carry
no `Version` — the state every project importing the kit is in, since the kit's pin file is where the
version is meant to come from — fails restore with NuGet's own **`NU1015`**, naming the packages with
no version and nothing else. It says nothing about central package management or a missing file, and
its ordinary fix, adding a `Version` to each reference, is the wrong one here: it papers over the
missing pin file rather than restoring it.

**That first message is not the kit's own guard — `ZZB002` never runs, because restore fails before
any MSBuild target does.** Measured: adding a version to make restore succeed does not silently
accept the change. The very next `dotnet build` reaches `ZeroZeroCheckKit` and fails on `ZZB002`,
naming the actual cause — `Directory.Packages.props` not importing `ZeroZero.Packages.props` — which
is the fix to make. A bare `dotnet restore` after adding the version does not reach it, since
`ZZB002` runs before build, not before restore.

So the path to the right fix costs one extra round trip when the pin file is missing and the
references are not yet versioned, not a wrong turn that sticks: the first error looks like an
ordinary NuGet complaint, and the second one is the kit's, and correct.
