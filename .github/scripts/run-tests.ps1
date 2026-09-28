<#
.SYNOPSIS
Runs the guards of the modules a change touched, or every test when asked for with -Full.

.DESCRIPTION
The one test-run definition. Nothing runs it automatically: the CI workflow calls it only from its
manual trigger, and the release workflow does not call it at all. docs/testing.md says what a
default run proves and, more to the point, what it leaves unproved.

A module is a component key: the first segment of a test project's name after "ZeroZero.",
lower-cased, as in a release tag. ZeroZero.Config.Watch.Tests belongs to config. A new test project
joins its module the moment its folder exists, so no list here needs editing.

What runs:
  (no switch)        the guards of the modules changed since -Since, uncommitted work included
  -Module mqtt, config  the guards of the modules named
  -AllModules           the guards of every module
  -Full                 every test instead of the guards; with no -Module, the whole suite

A guard is a test marked [Trait(Guard.Category, Guard.Value)]; tests/Guard.cs holds the mark.

The run fails, whatever the tests say, when: no project exists under tests/; a test project is
missing from the solution, so the build never produced it; no module was selected; a module name
is not a component; or, in a guards run, a selected project matched no guard at all.

Requires the solution to have been built in the given configuration first.
#>
# No positional binding: "-Module mqtt config" would otherwise bind config to -Since and run mqtt
# alone without a word. It is refused instead; the list takes commas, or one quoted string.
[CmdletBinding(PositionalBinding = $false)]
param(
    # Component keys to run. Omitted, the modules are worked out from what changed.
    [string[]]$Module,

    # Every test rather than the guards. Without -Module, every test of every module.
    [switch]$Full,

    # The guards of every module, whatever changed.
    [switch]$AllModules,

    # What the working tree is compared against when no module is named.
    [string]$Since = "origin/main",

    [string]$Configuration = "Release",
    [string]$Solution = "0z0-shared.slnx",
    [string]$TestsRoot = "tests"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

# The component-key rule, taken from the release scripts rather than restated here.
. (Join-Path $PSScriptRoot "component.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$solutionPath = Join-Path $repoRoot $Solution
$testsPath = Join-Path $repoRoot $TestsRoot

# Whatever is not inside a project with tests of its own — the workflows, the scripts, the build
# kit, the guides, the root files — is read by this module's tests, so a change there selects it.
$RepositoryModule = "releaseverification"

# The mark's two strings, as tests/Guard.cs declares them. A value that disagreed would select
# nothing, and the empty-selection check below turns that into a failure rather than a green run.
$GuardFilter = "Category=Guard"

$projects = @(Get-ChildItem -Path $testsPath -Filter *.csproj -Recurse -Depth 1 -File -ErrorAction SilentlyContinue |
    Sort-Object FullName)
if ($projects.Count -eq 0) {
    Write-Host "::error::No test project found under $TestsRoot. A run with nothing to test must not go green."
    exit 1
}

# The solution's project list, as forward-slash paths relative to the repository root.
[xml]$solutionXml = Get-Content $solutionPath -Raw
$inSolution = @($solutionXml.SelectNodes("//Project/@Path") | ForEach-Object { $_.Value -replace "\\", "/" })

$outside = @()
foreach ($project in $projects) {
    $relative = [System.IO.Path]::GetRelativePath($repoRoot, $project.FullName) -replace "\\", "/"
    if ($inSolution -notcontains $relative) { $outside += $relative }
}
if ($outside.Count -gt 0) {
    foreach ($path in $outside) {
        Write-Host "::error::$path is not in $Solution, so the solution build did not build it and its tests cannot run. Add it to the solution."
    }
    exit 1
}

$byModule = @{}
foreach ($project in $projects) {
    $key = Get-ComponentKey $project.BaseName
    if (-not $byModule.ContainsKey($key)) { $byModule[$key] = @() }
    $byModule[$key] += $project
}
$knownModules = @($byModule.Keys | Sort-Object)

function Resolve-ChangedModules {
    # Committed changes since the base, and the working tree on top of them: a local run is usually
    # about work not yet committed, so leaving that out would test the wrong thing.
    & git -C $repoRoot rev-parse --verify --quiet "$Since" *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::'$Since' is not a revision this checkout holds, so the changed modules cannot be worked out. Name them with -Module, give a base with -Since, or run every module with -AllModules."
        exit 1
    }

    $paths = @(& git -C $repoRoot diff --name-only "$Since...HEAD")
    if ($LASTEXITCODE -ne 0) { Write-Host "::error::git diff against '$Since' failed."; exit 1 }

    $paths += @(& git -C $repoRoot status --porcelain --untracked-files=all |
        ForEach-Object { $_.Substring(3).Trim('"') } |
        ForEach-Object { ($_ -split ' -> ')[-1] })

    $modules = @()
    foreach ($path in ($paths | Where-Object { $_ } | Sort-Object -Unique)) {
        $segments = ($path -replace "\\", "/") -split "/"
        $key = $null
        if ($segments.Count -ge 3 -and ($segments[0] -eq "src" -or $segments[0] -eq $TestsRoot)) {
            try { $key = Get-ComponentKey $segments[1] } catch { $key = $null }
            if ($key -and -not $byModule.ContainsKey($key)) { $key = $null }
        }
        $modules += $(if ($key) { $key } else { $RepositoryModule })
    }
    return @($modules | Sort-Object -Unique)
}

if ($AllModules -or ($Full -and -not $Module)) {
    $selectedModules = $knownModules
    $reason = "every module"
}
elseif ($Module) {
    # A component with no test project of its own — the build kit — is guarded by the module that
    # reads the repository's machinery. A name Versions.props does not declare either is a typo.
    $declared = Get-ComponentVersions
    $resolved = @()
    $unknown = @()
    foreach ($name in ($Module | ForEach-Object { $_ -split '[\s,]+' } | Where-Object { $_ })) {
        if ($byModule.ContainsKey($name)) { $resolved += $name }
        elseif ($declared.ContainsKey($name)) {
            Write-Host "$name has no test project of its own; the tests that guard it are $RepositoryModule's."
            $resolved += $RepositoryModule
        }
        else { $unknown += $name }
    }
    if ($unknown.Count -gt 0) {
        Write-Host "::error::$($unknown -join ', ') is not a module. The modules with tests are: $($knownModules -join ', ')."
        exit 1
    }
    $selectedModules = @($resolved | Sort-Object -Unique)
    $reason = "named"
}
else {
    $selectedModules = @(Resolve-ChangedModules)
    $reason = "changed since $Since"
    if ($selectedModules.Count -eq 0) {
        Write-Host "::error::Nothing has changed since $Since and nothing is uncommitted, so no module was selected and the run would test nothing. Name a module with -Module, or use -AllModules."
        exit 1
    }
}

$selected = @($selectedModules | ForEach-Object { $byModule[$_] } | Sort-Object FullName)
$scope = if ($Full) { "every test" } else { "the guards" }

Write-Host "Running $scope of $($selectedModules.Count) module(s): $($selectedModules -join ', ') ($reason)."
if (-not $Full) {
    Write-Host "A guards run proves the guards of these modules and nothing else. docs/testing.md lists what it leaves unproved."
}

$results = foreach ($project in $selected) {
    Write-Host ""
    Write-Host "=== $($project.BaseName) ==="

    $arguments = @($project.FullName, "-c", $Configuration, "--no-build", "--nologo")
    if (-not $Full) { $arguments += @("--filter", $GuardFilter) }

    & dotnet test @arguments | Tee-Object -Variable lines | Out-Host
    $exitCode = $LASTEXITCODE

    # The exit code decides, except for a filter that matched nothing: dotnet test exits zero then,
    # and only the count shows it. It prints one "Failed: n, Passed: n, Skipped: n, Total: n" line
    # per test assembly; they are summed.
    $counts = [ordered]@{ Passed = 0; Failed = 0; Skipped = 0; Total = 0 }
    $text = ($lines | ForEach-Object { "$_" }) -join "`n"
    foreach ($match in [regex]::Matches($text, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')) {
        $counts.Failed += [int]$match.Groups[1].Value
        $counts.Passed += [int]$match.Groups[2].Value
        $counts.Skipped += [int]$match.Groups[3].Value
        $counts.Total += [int]$match.Groups[4].Value
    }

    [pscustomobject]@{
        Project  = $project.BaseName
        Module   = Get-ComponentKey $project.BaseName
        ExitCode = $exitCode
        Passed   = $counts.Passed
        Failed   = $counts.Failed
        Skipped  = $counts.Skipped
        Total    = $counts.Total
    }
}

Write-Host ""
Write-Host "=== Test projects: $scope ==="
foreach ($result in $results) {
    $outcome = if ($result.ExitCode -eq 0) { "passed" } else { "FAILED (exit code $($result.ExitCode))" }
    Write-Host ("{0,-36} {1,-28} passed {2}, failed {3}, skipped {4}, total {5}" -f
        $result.Project, $outcome, $result.Passed, $result.Failed, $result.Skipped, $result.Total)
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @(
        "## Test projects: $scope of $($selectedModules -join ', ')",
        "",
        "| Project | Outcome | Passed | Failed | Skipped | Total |",
        "|---|---|---:|---:|---:|---:|")
    foreach ($result in $results) {
        $outcome = if ($result.ExitCode -eq 0) { "passed" } else { "**failed**" }
        $summary += "| $($result.Project) | $outcome | $($result.Passed) | $($result.Failed) | $($result.Skipped) | $($result.Total) |"
    }
    if (-not $Full) {
        $summary += @("", "A guards run: modules not listed did not run, and in those listed only the guards did. See docs/testing.md for what that leaves unproved.")
    }
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($summary -join "`n")
}

$failed = @($results | Where-Object { $_.ExitCode -ne 0 })
if ($failed.Count -gt 0) {
    foreach ($result in $failed) {
        Write-Host "::error::$($result.Project) failed: $($result.Failed) failed of $($result.Total) (dotnet test exit code $($result.ExitCode))."
    }
    exit 1
}

# A project whose guards were unmarked, or marked with a value the filter does not name, would
# otherwise pass having run nothing — the one way this arrangement could go quiet.
if (-not $Full) {
    $empty = @($results | Where-Object { $_.Total -eq 0 })
    if ($empty.Count -gt 0) {
        foreach ($result in $empty) {
            Write-Host "::error::$($result.Project) matched no guard, so it ran no test and proved nothing. Every test project carries at least one [Trait(Guard.Category, Guard.Value)]; see tests/Guard.cs."
        }
        exit 1
    }
}

$total = ($results | Measure-Object -Property Total -Sum).Sum
Write-Host "All $($results.Count) test projects passed, $total tests: $scope of $($selectedModules -join ', ')."
exit 0
