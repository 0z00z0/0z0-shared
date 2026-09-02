<#
.SYNOPSIS
Runs every test project under tests/ and fails at the end naming each project that failed.

.DESCRIPTION
The one test-run definition, shared by ci.yml and release.yml through .github/actions/run-tests.
Projects are discovered by glob when the script runs, so a test project cannot be left out of the
run: it is in the moment its folder exists. Each project runs on its own so a failure names its
project, and every project runs even after one fails so the report is complete.

Two conditions fail the run before a single test executes. No project under tests/: a glob that
matches nothing would be a green run with no tests, the exact failure this script exists to prevent.
A project missing from the solution: the workflows build the solution and run the tests with
--no-build, so a project outside the solution has no binaries and its tests would never run.

Requires the solution to have been built in the given configuration first.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Solution = "0z0-shared.slnx",
    [string]$TestsRoot = "tests"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$solutionPath = Join-Path $repoRoot $Solution
$testsPath = Join-Path $repoRoot $TestsRoot

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

$results = foreach ($project in $projects) {
    Write-Host ""
    Write-Host "=== $($project.BaseName) ==="
    & dotnet test $project.FullName -c $Configuration --no-build --nologo | Tee-Object -Variable lines | Out-Host
    $exitCode = $LASTEXITCODE

    # The totals line is informational — the exit code is what decides. dotnet test prints one
    # "Failed: n, Passed: n, Skipped: n, Total: n" line per test assembly; they are summed.
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
        ExitCode = $exitCode
        Passed   = $counts.Passed
        Failed   = $counts.Failed
        Skipped  = $counts.Skipped
        Total    = $counts.Total
    }
}

Write-Host ""
Write-Host "=== Test projects ==="
foreach ($result in $results) {
    $outcome = if ($result.ExitCode -eq 0) { "passed" } else { "FAILED (exit code $($result.ExitCode))" }
    Write-Host ("{0,-36} {1,-28} passed {2}, failed {3}, skipped {4}, total {5}" -f
        $result.Project, $outcome, $result.Passed, $result.Failed, $result.Skipped, $result.Total)
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @("## Test projects", "", "| Project | Outcome | Passed | Failed | Skipped | Total |", "|---|---|---:|---:|---:|---:|")
    foreach ($result in $results) {
        $outcome = if ($result.ExitCode -eq 0) { "passed" } else { "**failed**" }
        $summary += "| $($result.Project) | $outcome | $($result.Passed) | $($result.Failed) | $($result.Skipped) | $($result.Total) |"
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

$total = ($results | Measure-Object -Property Total -Sum).Sum
Write-Host "All $($results.Count) test projects passed, $total tests."
exit 0
