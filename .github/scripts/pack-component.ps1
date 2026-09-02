<#
.SYNOPSIS
Packs the projects of the component a tag names, and nothing else, then checks what landed.

.DESCRIPTION
Every packable project under src/ whose key matches the tag's is packed with --no-build into the
output folder. Afterwards the folder must hold exactly one <Name>.<version>.nupkg per selected
project: a missing package is a pack that silently produced nothing, and an extra one is a stale or
foreign package that the push step would publish under this release. Both fail.

The output folder must be empty of packages before the run for the same reason.

Once the set is right, the script writes release-artefacts.json beside the packages: the tag, the
commit and the SHA-256 of every package as packed. That file is the build's own statement of what
it produced, and verify-release.ps1 holds what was published to it after the push. The hash is
taken here and never again — recomputed later from a rebuilt artefact it would agree with itself.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Output = "artifacts",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot "component.ps1")

function Fail([string]$Message) {
    Write-Host "::error::$Message"
    exit 1
}

try { $release = Read-ReleaseTag $Tag } catch { Fail $_.Exception.Message }

# @() on both: a function returning an empty array hands the caller $null, which has no Count.
$projects = @(Get-ComponentProjects $release.Key @(Get-SourceProjects))
if ($projects.Count -eq 0) {
    Fail "No packable project under src/ has the key '$($release.Key)', so there is nothing to pack."
}

$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location).Path $Output }
if (Test-Path $outputPath) {
    $stale = @(Get-ChildItem $outputPath -Filter *.nupkg -File)
    if ($stale.Count -gt 0) {
        Fail "$outputPath already holds $($stale.Count) package(s); a stale package could be pushed as this release's. Empty it first."
    }
}

foreach ($project in $projects) {
    Write-Host "=== pack $($project.Name) ==="
    & dotnet pack $project.Path -c $Configuration --no-build -o $outputPath
    if ($LASTEXITCODE -ne 0) { Fail "dotnet pack failed for $($project.Name) (exit code $LASTEXITCODE)." }
}

$expected = @($projects | ForEach-Object { "$($_.Name).$($release.Version).nupkg" })
$actual = @(Get-ChildItem $outputPath -Filter *.nupkg -File | ForEach-Object Name)

$wrong = $false
foreach ($name in $expected) {
    if ($actual -notcontains $name) {
        Write-Host "::error::$name was not produced."
        $wrong = $true
    }
}
foreach ($name in $actual) {
    if ($expected -notcontains $name) {
        Write-Host "::error::$name is in $outputPath but is not part of this release."
        $wrong = $true
    }
}
if ($wrong) { exit 1 }

# The commit the packages describe, from the checkout itself so a local rehearsal records the
# same thing a runner does. On a tag push it is the tag's commit.
$commit = & git -C $script:RepoRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$commit")) {
    Fail "git rev-parse HEAD failed in $script:RepoRoot; the record needs the commit the packages describe."
}
$record = [ordered]@{
    tag       = $Tag
    key       = $release.Key
    version   = $release.Version
    commit    = "$commit".Trim()
    artefacts = @(foreach ($project in $projects) {
        $name = "$($project.Name).$($release.Version).nupkg"
        [ordered]@{
            name    = $name
            id      = $project.Name
            version = $release.Version
            sha256  = (Get-FileHash -LiteralPath (Join-Path $outputPath $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$recordPath = Join-Path $outputPath "release-artefacts.json"
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $recordPath -Encoding utf8

Write-Host "Packed $($expected.Count) package(s) for $($release.Key) $($release.Version): $($expected -join ', ')."
Write-Host "Recorded their hashes at commit $($record.commit) in $recordPath."
exit 0
