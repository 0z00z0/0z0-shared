<#
.SYNOPSIS
Fetches what was published and asserts it is what this build produced.

.DESCRIPTION
pack-component.ps1 writes release-artefacts.json: the tag, the commit and the SHA-256 of every
package as packed. This script takes that record and the place the artefacts were published,
fetches each one from there, and looks.

Bytes     The SHA-256 of what came down equals the hash the build recorded. This is the assertion
          that closes the loop: a feed can hold a well-formed package at the right version that is
          not this build, and every shape check passes on it.
Identity  Inside a package, the nuspec id and version are the artefact's own, and the nuspec's
          repository commit is the commit being released.
Stamp     Every assembly under lib/ reports the released version and that same commit, so a
          package packed from a stale build is refused even when its nuspec is right.
Manifest  With -Manifest: the manifest's version key equals the released version; its URL key
          contains the version and names a file this build produced; the URL is fetched, and the
          bytes hash to the manifest's declared hash and to the build's. A manifest that reads
          correctly, points at a real file and matches that file's hash still fails here when the
          file is another build's — the case no schema check can see.
Signed    With -RequireSigned, the recorded outcome of a step in the caller's job, read from
          steps.<id>.outcome, is the evidence that signing happened — in either of two forms. The
          step that signs, -SigningOutcome, must be "success": skipped means it never ran and
          nothing was signed. The warn-only step that says an installer is unsigned,
          -UnsignedOutcome, must be "skipped": that step cannot fail a job, so its having run is
          the only record that the installer shipped unsigned, and its being skipped the only
          evidence that it did not. Given without -RequireSigned, a step that ran and did not
          complete still fails. Both forms may be given, and each must hold.
Signer    With -Signer: every executable fetched carries an intact Authenticode signature whose
          subject is the one expected. A subject is a string anyone can put on a self-signed
          certificate, so -SignerThumbprint pins the certificate itself where the release signs
          with one no runner's root store trusts.

Every assertion fails closed: a location that cannot be reached, a record naming another tag or
commit, an empty record, a package without a nuspec — each is a failure, never a skip. The token
for a feed that authenticates reads arrives in ZEROZERO_FEED_TOKEN, is sent over https only, and
is never printed. -Report writes what was resolved and what came down, per artefact.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    # The record pack-component.ps1 wrote beside the packages.
    [Parameter(Mandatory)][string]$Artefacts,
    # The commit being released. On a runner GITHUB_SHA is the tag's commit.
    [string]$Commit = $env:GITHUB_SHA,
    # Where the artefacts were published: a URL or a folder. Every artefact in the record is
    # fetched from beneath it.
    [string]$Location,
    # Flat: <location>/<name>. NuGet: <location>/<id>/<version>/<id>.<version>.nupkg, lower-cased,
    # which is the v3 package base address GitHub Packages serves.
    [ValidateSet("Flat", "NuGet")][string]$Layout = "Flat",
    # Manifests whose URL, hash and version keys must describe this build.
    [string[]]$Manifest = @(),
    [string]$UrlKey = "InstallerUrl",
    [string]$HashKey = "InstallerSha256",
    [string]$VersionKey = "PackageVersion",
    # steps.<id>.outcome of the step that signs: success, failure, cancelled or skipped.
    [string]$SigningOutcome,
    # steps.<id>.outcome of a warn-only unsigned-installer step, whose "skipped" is the evidence
    # that signing happened.
    [string]$UnsignedOutcome,
    [switch]$RequireSigned,
    # The subject every fetched executable must be signed by, and the thumbprint of that
    # certificate where the subject alone would not identify it.
    [string]$Signer,
    [string]$SignerThumbprint,
    [string]$Report
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "manifest.ps1")

# Named apart from the -Report parameter: PowerShell variable names are case-insensitive, and the
# script scope is the parameter's scope.
$script:failures = 0
$script:findings = @()
$script:tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-release-" + [guid]::NewGuid().ToString("n"))

function Fail([string]$Message) {
    Write-Host "::error::$Message"
    $script:failures++
}

function Stop-Verification([string]$Message) {
    Write-Host "::error::$Message"
    exit 1
}

function Get-Field($Object, [string]$Name) {
    # A record field, or $null when absent; strict mode throws on a missing property otherwise.
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Read-Record([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-Verification "The record $Path does not exist. pack-component.ps1 writes it beside the packages; without it there is nothing to verify against."
    }
    try { $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { Stop-Verification "The record $Path is not JSON: $($_.Exception.Message)" }

    foreach ($field in "tag", "version", "commit") {
        if ([string]::IsNullOrWhiteSpace((Get-Field $record $field))) {
            Stop-Verification "The record $Path has no '$field'."
        }
    }
    $entries = @(Get-Field $record "artefacts")
    if ($entries.Count -eq 0) {
        Stop-Verification "The record $Path lists no artefacts, so this release published nothing that can be verified."
    }
    foreach ($entry in $entries) {
        $name = Get-Field $entry "name"
        $hash = Get-Field $entry "sha256"
        if ([string]::IsNullOrWhiteSpace($name)) { Stop-Verification "An artefact in $Path has no name." }
        if ("$hash" -notmatch '^[0-9a-fA-F]{64}$') { Stop-Verification "Artefact '$name' in $Path has no SHA-256 ('$hash')." }
    }
    return $record
}

function Resolve-Source([string]$Base, $Entry) {
    $isUrl = $Base -match '^https?://'
    $root = $Base.TrimEnd('/', '\')
    $name = Get-Field $Entry "name"
    switch ($Layout) {
        "Flat" {
            if ($isUrl) { return "$root/$name" }
            return Join-Path $root $name
        }
        "NuGet" {
            $id = Get-Field $Entry "id"
            $version = Get-Field $Entry "version"
            if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($version)) {
                throw "Artefact '$name' has no id or version in the record, and the NuGet layout needs both."
            }
            $id = $id.ToLowerInvariant()
            $version = $version.ToLowerInvariant()
            if ($isUrl) { return "$root/$id/$version/$id.$version.nupkg" }
            return Join-Path $root $id $version "$id.$version.nupkg"
        }
    }
}

function Get-Fetched([string]$Source, [string]$Name) {
    # The bytes as published, under the artefact's own name so a signature check sees the right
    # file type. Hashed once, here.
    $dir = Join-Path $script:tempRoot ([guid]::NewGuid().ToString("n"))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $target = Join-Path $dir $Name

    if ($Source -match '^https?://') {
        $headers = @{}
        if ($Source -match '^https://' -and -not [string]::IsNullOrEmpty($env:ZEROZERO_FEED_TOKEN)) {
            $headers["Authorization"] = "Bearer $env:ZEROZERO_FEED_TOKEN"
        }
        try { Invoke-WebRequest -Uri $Source -Headers $headers -OutFile $target -MaximumRetryCount 2 -RetryIntervalSec 3 | Out-Null }
        catch { throw "Fetching $Source failed: $($_.Exception.Message)" }
    }
    else {
        $path = if ($Source -match '^file:') { ([uri]$Source).LocalPath } else { $Source }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$path does not exist; nothing was published there."
        }
        Copy-Item -LiteralPath $path -Destination $target
    }

    return [pscustomobject]@{
        Path   = $target
        Sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        Source = $Source
    }
}

function Test-Package([string]$Path, $Entry) {
    $name = Get-Field $Entry "name"
    $expectedId = Get-Field $Entry "id"
    $expectedVersion = Get-Field $Entry "version"
    if ([string]::IsNullOrWhiteSpace($expectedId) -or [string]::IsNullOrWhiteSpace($expectedVersion)) {
        Fail "$name is a package but the record gives it no id or version, so its nuspec cannot be checked."
        return
    }

    $zip = $null
    try { $zip = [System.IO.Compression.ZipFile]::OpenRead($Path) }
    catch { Fail "$name is not a readable package: $($_.Exception.Message)"; return }
    try {
        $nuspecs = @($zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName -like '*.nuspec' })
        if ($nuspecs.Count -ne 1) {
            Fail "$name holds $($nuspecs.Count) nuspec files at its root; a package holds exactly one."
            return
        }
        $reader = [System.IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata

        $id = "$(Get-Field $metadata 'id')"
        $version = "$(Get-Field $metadata 'version')"
        if ($id -cne $expectedId) { Fail "$name's nuspec says id '$id'; the artefact is $expectedId." }
        if ($version -ne $expectedVersion) { Fail "$name's nuspec says version '$version'; the release is $expectedVersion." }

        $repository = Get-Field $metadata "repository"
        $nuspecCommit = if ($null -ne $repository -and $repository -is [System.Xml.XmlElement]) { $repository.GetAttribute("commit") } else { "" }
        if ([string]::IsNullOrWhiteSpace($nuspecCommit)) {
            Fail "$name's nuspec names no repository commit, so it cannot say which build it is."
        }
        elseif ($nuspecCommit -ne $Commit) {
            Fail "$name's nuspec says commit $nuspecCommit; the release is $Commit. The package describes another build."
        }
        else {
            Write-Host "  nuspec: $id $version at $nuspecCommit."
        }

        $assemblies = @($zip.Entries | Where-Object { $_.FullName -match '^lib/.+\.dll$' })
        if ($assemblies.Count -eq 0) {
            Write-Host "  no assembly under lib/, so no stamp to read."
        }
        foreach ($assembly in $assemblies) {
            $extracted = Join-Path (Split-Path -Parent $Path) ("stamp-" + [guid]::NewGuid().ToString("n") + "-" + $assembly.Name)
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($assembly, $extracted, $true)
            $product = "$((Get-Item -LiteralPath $extracted).VersionInfo.ProductVersion)"
            if ($product -notmatch '^(?<version>[^+]+)\+(?<sha>[0-9a-fA-F]{7,40})$') {
                Fail "$($assembly.FullName) in $name reports '$product'; the family stamps <version>+<commit>, so this assembly cannot say which build it is."
                continue
            }
            if ($Matches['version'] -ne $expectedVersion) {
                Fail "$($assembly.FullName) in $name reports version $($Matches['version']); the release is $expectedVersion."
            }
            elseif (-not $Commit.StartsWith($Matches['sha'], [System.StringComparison]::OrdinalIgnoreCase)) {
                Fail "$($assembly.FullName) in $name was built at commit $($Matches['sha']); the release is $Commit. The package was packed from a stale build."
            }
            else {
                Write-Host "  $($assembly.FullName): $product."
            }
        }
    }
    finally { $zip.Dispose() }
}

function Test-Signer([string]$Path, [string]$Name) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($null -eq $signature.SignerCertificate) {
        Fail "$Name is not signed (status $($signature.Status)); the release requires a signature by '$Signer'."
        return
    }
    $subject = $signature.SignerCertificate.Subject
    if ($subject -ne $Signer) {
        Fail "$Name is signed by '$subject', not by '$Signer'."
        return
    }
    $thumbprint = $signature.SignerCertificate.Thumbprint
    if ($SignerThumbprint -and $thumbprint -ne $SignerThumbprint) {
        Fail "$Name is signed by a certificate with thumbprint $thumbprint, not $SignerThumbprint. The subject '$subject' is right and the certificate is not the release's."
        return
    }
    # A self-signed studio certificate reads as an untrusted root on a machine that has not been
    # told to trust it; the signature itself is intact. Anything else is a broken signature.
    $untrustedRoot = $signature.Status -eq "UnknownError" -and $signature.StatusMessage -match "not trusted"
    if ($signature.Status -ne "Valid" -and -not $untrustedRoot) {
        Fail "$Name's signature verifies as $($signature.Status): $($signature.StatusMessage)"
        return
    }
    Write-Host "  signed by '$subject', thumbprint $thumbprint ($($signature.Status))."
}

function Test-Contents($Fetched, $Entry) {
    $name = Get-Field $Entry "name"
    if ($name -like "*.nupkg") { Test-Package $Fetched.Path $Entry }
    if ($Signer -and ($name -match '\.(exe|dll|msi)$')) { Test-Signer $Fetched.Path $name }
}

function Add-Finding([string]$Name, [string]$Source, [string]$Sha256, [bool]$Passed) {
    $script:findings += [ordered]@{ name = $Name; source = $Source; sha256 = $Sha256; passed = $Passed }
}

# ---- the checks ----

if ([string]::IsNullOrWhiteSpace($Location) -and $Manifest.Count -eq 0) {
    Stop-Verification "Nothing to verify against: give -Location, -Manifest or both."
}

$hasSigningOutcome = -not [string]::IsNullOrWhiteSpace($SigningOutcome)
$hasUnsignedOutcome = -not [string]::IsNullOrWhiteSpace($UnsignedOutcome)
if ($RequireSigned -and -not $hasSigningOutcome -and -not $hasUnsignedOutcome) {
    Fail "Signing is required and no signing outcome was given. Pass steps.<id>.outcome of the step that signs as -SigningOutcome, or of the warn-only unsigned step as -UnsignedOutcome; a green job is not evidence that either ran."
}
if ($hasSigningOutcome) {
    if ($SigningOutcome -eq "success") {
        Write-Host "The signing step's recorded outcome is success."
    }
    elseif ($RequireSigned) {
        Fail "Signing is required and the signing step's recorded outcome is '$SigningOutcome'. Skipped means the step never ran and nothing was signed; a warn-only step cannot say otherwise."
    }
    elseif ($SigningOutcome -eq "skipped") {
        Write-Host "Signing is not required; the signing step's recorded outcome is skipped, so nothing was signed."
    }
    else {
        Fail "The signing step's recorded outcome is '$SigningOutcome'; a signing step that ran and did not succeed fails the release whether or not signing is required."
    }
}
if ($hasUnsignedOutcome) {
    if ($UnsignedOutcome -eq "skipped") {
        Write-Host "The unsigned-installer step's recorded outcome is skipped, so the installer was signed."
    }
    elseif ($UnsignedOutcome -ne "success") {
        Fail "The unsigned-installer step's recorded outcome is '$UnsignedOutcome'; a step that ran and did not complete fails the release whether or not signing is required."
    }
    elseif ($RequireSigned) {
        Fail "Signing is required and the unsigned-installer step ran, so the installer shipped unsigned. That step only warns and cannot fail the job; its being skipped is the only evidence of signing."
    }
    else {
        Write-Host "Signing is not required; the unsigned-installer step ran, so the installer is unsigned."
    }
}

if ([string]::IsNullOrWhiteSpace($Commit)) {
    Stop-Verification "No commit given and GITHUB_SHA is not set; the release cannot be tied to a build."
}

$record = Read-Record $Artefacts
$recordTag = "$(Get-Field $record 'tag')"
$recordVersion = "$(Get-Field $record 'version')"
$recordCommit = "$(Get-Field $record 'commit')"
$entries = @(Get-Field $record "artefacts")

if ($recordTag -cne $Tag) {
    Stop-Verification "The record describes tag '$recordTag'; the release is '$Tag'. The record is another release's."
}
if ($Tag -cnotmatch ('v' + [regex]::Escape($recordVersion) + '$')) {
    Stop-Verification "Tag '$Tag' does not end in v$recordVersion, the version the record says was packed."
}
if ($recordCommit -ne $Commit) {
    Stop-Verification "The record was written at commit $recordCommit; the release is $Commit. The record is another build's."
}

$byName = @{}
foreach ($entry in $entries) { $byName[(Get-Field $entry "name")] = $entry }

New-Item -ItemType Directory -Path $script:tempRoot | Out-Null
try {
    if (-not [string]::IsNullOrWhiteSpace($Location)) {
        foreach ($entry in $entries) {
            $name = Get-Field $entry "name"
            $expected = (Get-Field $entry "sha256").ToLowerInvariant()
            Write-Host "=== $name ==="
            $failuresBefore = $script:failures
            try { $source = Resolve-Source $Location $entry } catch { Fail $_.Exception.Message; Add-Finding $name "" "" $false; continue }
            Write-Host "  from $source"
            try { $fetched = Get-Fetched $source $name } catch { Fail $_.Exception.Message; Add-Finding $name $source "" $false; continue }

            if ($fetched.Sha256 -eq $expected) {
                Write-Host "  bytes: $($fetched.Sha256), the build's."
            }
            else {
                Fail "What came down from $source hashes to $($fetched.Sha256); the build recorded $expected for $name. What is published is not this build."
            }
            # Inspected even after a hash mismatch: a nuspec and a stamp that are right say the feed
            # holds a stale pack of this commit, and ones that are wrong say another build.
            Test-Contents $fetched $entry
            Add-Finding $name $source $fetched.Sha256 ($script:failures -eq $failuresBefore)
        }
    }

    foreach ($path in $Manifest) {
        Write-Host "=== manifest $path ==="
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail "Manifest $path does not exist."; continue }
        try {
            $declaredVersion = Get-ManifestValue $path $VersionKey
            $url = Get-ManifestValue $path $UrlKey
            $declaredHash = Get-ManifestValue $path $HashKey
        }
        catch { Fail $_.Exception.Message; continue }

        if ($declaredVersion -ne $recordVersion) {
            Fail "$path declares $VersionKey '$declaredVersion'; the release is $recordVersion."
        }
        if ($url -notmatch [regex]::Escape($recordVersion)) {
            Fail "$path's $UrlKey '$url' does not contain the version $recordVersion, so it is not this release's download."
        }
        $name = [uri]::UnescapeDataString(($url -split '[/\\]')[-1])
        if (-not $byName.ContainsKey($name)) {
            Fail "$path's $UrlKey names '$name', which this build did not produce. The build produced: $(($entries | ForEach-Object { Get-Field $_ 'name' }) -join ', ')."
            continue
        }
        if ($declaredHash -notmatch '^[0-9a-fA-F]{64}$') {
            Fail "$path's $HashKey '$declaredHash' is not a SHA-256; a placeholder that was never rewritten looks like this."
            continue
        }
        $entry = $byName[$name]
        $expected = (Get-Field $entry "sha256").ToLowerInvariant()
        $failuresBefore = $script:failures
        Write-Host "  from $url"
        try { $fetched = Get-Fetched $url $name } catch { Fail $_.Exception.Message; Add-Finding $name $url "" $false; continue }

        if ($fetched.Sha256 -ne $declaredHash.ToLowerInvariant()) {
            Fail "$path declares $HashKey $declaredHash; what came down from $url hashes to $($fetched.Sha256). The manifest does not describe what it points at."
        }
        if ($fetched.Sha256 -ne $expected) {
            Fail "What came down from $url hashes to $($fetched.Sha256); the build recorded $expected for $name. The manifest describes a file that is not this build."
        }
        if ($script:failures -eq $failuresBefore) { Write-Host "  bytes: $($fetched.Sha256), the manifest's and the build's." }
        Test-Contents $fetched $entry
        Add-Finding $name $url $fetched.Sha256 ($script:failures -eq $failuresBefore)
    }
}
finally {
    Remove-Item -LiteralPath $script:tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not [string]::IsNullOrWhiteSpace($Report)) {
    [pscustomobject]@{ tag = $Tag; commit = $Commit; passed = ($script:failures -eq 0); artefacts = @($script:findings) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Report -Encoding utf8
}

if ($script:failures -gt 0) {
    Write-Host "::error::$Tag at $Commit is not verified: what is published does not describe this build ($($script:failures) finding(s) above)."
    exit 1
}
Write-Host "Verified $Tag at ${Commit}: every artefact fetched is the build's own."
exit 0
