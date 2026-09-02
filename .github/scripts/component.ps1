# Shared by the release scripts: which component a tag names, which projects belong to it, and what
# each of them declares. Dot-source it; it defines functions and one variable, and runs nothing.
#
# The component key is the first segment of an assembly name after "ZeroZero." — ZeroZero.Mqtt.WinUI
# is mqtt — lower-cased. It names the tag prefix (mqtt-v0.7.0), the Versions.props property
# (MqttVersion) and the release-notes folder (docs/release-notes/mqtt/). A project is part of a
# release when its key matches and it packs; the harness never packs, so no name needs an exception.

Set-StrictMode -Version Latest

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

function Get-ComponentKey([string]$ProjectName) {
    if ($ProjectName -notmatch '^ZeroZero\.([A-Za-z0-9]+)(\.|$)') {
        throw "'$ProjectName' does not follow ZeroZero.<Component>[.<Part>], so it belongs to no component."
    }
    return $Matches[1].ToLowerInvariant()
}

function Read-ReleaseTag([string]$Tag) {
    # Case-sensitive: the key is lower-case by definition, and an upper-case tag would release under
    # a name no dependency check ever looks for.
    if ($Tag -cnotmatch '^(?<key>[a-z][a-z0-9]*)-v(?<version>\d+\.\d+\.\d+)$') {
        throw "Tag '$Tag' is not <component>-v<x.y.z>. A tag names one component, as in mqtt-v0.7.0; a bare v0.7.0 names none, an upper-case key is not the key, and both are refused."
    }
    return [pscustomobject]@{ Tag = $Tag; Key = $Matches['key']; Version = $Matches['version'] }
}

function Get-ComponentVersions {
    # Versions.props as a table: brand → 0.7.0, from the property BrandVersion.
    [xml]$props = Get-Content (Join-Path $script:RepoRoot "Versions.props") -Raw
    $versions = @{}
    foreach ($node in $props.SelectNodes("/Project/PropertyGroup/*")) {
        if ($node.Name -match '^(?<name>[A-Za-z0-9]+)Version$') {
            $versions[$Matches['name'].ToLowerInvariant()] = $node.InnerText.Trim()
        }
    }
    return $versions
}

function Get-ProjectFacts([string]$ProjectPath) {
    # One MSBuild evaluation per project: what it declares and what it references. Evaluated rather
    # than parsed, so a Version set any other way than through Versions.props is still the value seen.
    $json = & dotnet msbuild $ProjectPath -getProperty:Version -getProperty:IsPackable -getItem:ProjectReference -nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild could not evaluate $ProjectPath." }
    $data = ($json -join "`n") | ConvertFrom-Json

    $references = @()
    if ($data.Items.PSObject.Properties['ProjectReference']) {
        $references = @($data.Items.ProjectReference | ForEach-Object {
            [System.IO.Path]::GetFileNameWithoutExtension($_.FullPath)
        })
    }

    $name = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return [pscustomobject]@{
        Name       = $name
        Path       = $ProjectPath
        Key        = Get-ComponentKey $name
        Version    = $data.Properties.Version
        IsPackable = $data.Properties.IsPackable -ne "false"
        References = $references
    }
}

function Get-SourceProjects {
    # Every project under src/, evaluated. The harness is among them and is excluded by IsPackable
    # wherever a release set is built, never by name.
    return @(Get-ChildItem (Join-Path $script:RepoRoot "src") -Filter *.csproj -Recurse -Depth 1 -File |
        Sort-Object FullName |
        ForEach-Object { Get-ProjectFacts $_.FullName })
}

function Get-ComponentProjects([string]$Key, [object[]]$All) {
    return @($All | Where-Object { $_.Key -eq $Key -and $_.IsPackable })
}

function Test-RemoteTag([string]$Remote, [string]$Name) {
    # The remote is the authority: a runner's checkout is shallow and holds only the tag that
    # triggered it. A remote that cannot be reached is an error, not an absent tag.
    $lines = @(& git -C $script:RepoRoot ls-remote --tags $Remote "refs/tags/$Name")
    if ($LASTEXITCODE -ne 0) { throw "git ls-remote against '$Remote' failed; the tag check needs the remote." }
    $pattern = "\trefs/tags/" + [regex]::Escape($Name) + '$'
    return @($lines | Where-Object { $_ -match $pattern }).Count -gt 0
}
