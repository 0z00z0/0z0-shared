<#
.SYNOPSIS
Stops a tagged run whose signing secret is absent, so a release cannot ship unsigned in silence.

.DESCRIPTION
Only the presence of the secret is read, from RELEASE_SIGNING_SECRET — never an argument, never
printed. On a tag the secret must be there, and the run fails otherwise. On a branch an absent
secret is allowed and said so, because a pull-request build carries no secrets and must still
build; what it produces is not a release. A ref type the script does not know is a run it cannot
place, and fails.

The gate answers whether signing can happen. Whether it did happen is the recorded outcome of the
signing step itself, which verify-release.ps1 asserts on: a warn-only step cannot fail a job, so
a green run is not that evidence.
#>
[CmdletBinding()]
param(
    # github.ref_type: "tag" or "branch".
    [Parameter(Mandatory)][string]$RefType
)

$ErrorActionPreference = "Stop"
$present = -not [string]::IsNullOrWhiteSpace($env:RELEASE_SIGNING_SECRET)

switch ($RefType) {
    "tag" {
        if (-not $present) {
            Write-Host "::error::This is a tagged run and RELEASE_SIGNING_SECRET is empty, so the release would ship unsigned. Add the secret to the repository, then re-run the tag."
            exit 1
        }
        Write-Host "Tagged run; the signing secret is present."
    }
    "branch" {
        if ($present) { Write-Host "Branch run; the signing secret is present." }
        else { Write-Host "Branch run with no signing secret, so nothing signs. Nothing from this run is a release." }
    }
    default {
        Write-Host "::error::Ref type '$RefType' is neither tag nor branch; the gate cannot tell whether this run ships, so it does not."
        exit 1
    }
}

exit 0
