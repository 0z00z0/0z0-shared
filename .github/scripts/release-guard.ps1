<#
.SYNOPSIS
One release precondition per call, so the workflow step that goes red names the check.

.DESCRIPTION
Tag           The tag is <component>-v<x.y.z> and Versions.props declares that component. Writes
              key and version to GITHUB_OUTPUT when running in a workflow.
Notes         docs/release-notes/<key>/v<x.y.z>.md exists.
Versions      <Key>Version in Versions.props and the evaluated Version of every packable project of
              that key equal the tag's version, and at least one such project exists.
Dependencies  Every project reference from the released set to another component names a version
              whose tag <other>-v<version> exists on the remote — packing turns that reference into
              a package dependency, so the version has to be on the feed already.

Every check fails closed: a check that cannot be made is a failed check, never a skipped one.
Run from any directory; paths are resolved from the script's own location.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][ValidateSet("Tag", "Notes", "Versions", "Dependencies")][string]$Check,
    # The remote whose tags decide Dependencies. A path to a repository works as well as a name.
    [string]$Remote = "origin"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot "component.ps1")

function Fail([string]$Message) {
    Write-Host "::error::$Message"
    exit 1
}

try { $release = Read-ReleaseTag $Tag } catch { Fail $_.Exception.Message }
$key = $release.Key
$version = $release.Version
$versions = Get-ComponentVersions
$property = $key.Substring(0, 1).ToUpperInvariant() + $key.Substring(1) + "Version"

switch ($Check) {
    "Tag" {
        if (-not $versions.ContainsKey($key)) {
            $known = ($versions.Keys | Sort-Object) -join ", "
            Fail "Tag '$Tag' names component '$key', but Versions.props declares no $property. Known components: $known."
        }
        if ($env:GITHUB_OUTPUT) {
            Add-Content -Path $env:GITHUB_OUTPUT -Value "key=$key"
            Add-Content -Path $env:GITHUB_OUTPUT -Value "version=$version"
        }
        Write-Host "Tag '$Tag' names component '$key' at $version."
    }

    "Notes" {
        $notes = "docs/release-notes/$key/v$version.md"
        if (-not (Test-Path (Join-Path $script:RepoRoot $notes))) {
            Fail "$notes is missing. Write the release notes, then retag."
        }
        Write-Host "Found $notes."
    }

    "Versions" {
        if (-not $versions.ContainsKey($key)) {
            Fail "Versions.props declares no $property."
        }
        if ($versions[$key] -ne $version) {
            Fail "Versions.props declares $property $($versions[$key]); the tag says $version."
        }
        # @() on both: a function returning an empty array hands the caller $null, which has no Count.
        $projects = @(Get-ComponentProjects $key @(Get-SourceProjects))
        if ($projects.Count -eq 0) {
            Fail "No packable project under src/ has the key '$key', so the tag would release nothing."
        }
        $mismatch = $false
        foreach ($project in $projects) {
            if ($project.Version -ne $version) {
                Write-Host "::error::$($project.Name) declares '$($project.Version)', the tag says '$version'."
                $mismatch = $true
            }
        }
        if ($mismatch) { exit 1 }
        Write-Host "Every $key project declares ${version}: $(($projects | ForEach-Object Name) -join ', ')."
    }

    "Dependencies" {
        $all = @(Get-SourceProjects)
        $projects = @(Get-ComponentProjects $key $all)
        if ($projects.Count -eq 0) {
            Fail "No packable project under src/ has the key '$key', so the tag would release nothing."
        }
        $byName = @{}
        foreach ($project in $all) { $byName[$project.Name] = $project }

        $missing = $false
        $checked = 0
        foreach ($project in $projects) {
            foreach ($referenceName in $project.References) {
                if (-not $byName.ContainsKey($referenceName)) {
                    Fail "$($project.Name) references $referenceName, which is not a project under src/."
                }
                $target = $byName[$referenceName]
                if ($target.Key -eq $key) { continue }
                $checked++
                $needed = "$($target.Key)-v$($target.Version)"
                try { $found = Test-RemoteTag $Remote $needed } catch { Fail $_.Exception.Message }
                if ($found) {
                    Write-Host "$($project.Name) -> $($target.Name) $($target.Version): tag $needed exists on $Remote."
                }
                else {
                    Write-Host "::error::$($project.Name) references $($target.Name) at $($target.Version), but tag $needed does not exist on $Remote. Release $($target.Key) first."
                    $missing = $true
                }
            }
        }
        if ($missing) { exit 1 }
        Write-Host "Every component that $key depends on is released ($checked cross-component references checked)."
    }
}

exit 0
