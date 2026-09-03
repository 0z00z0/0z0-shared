# One value in a text manifest, read or rewritten — a winget YAML, or any file of key: value lines.
# Dot-source it; it defines functions and runs nothing.
#
# The invariant is that a key matches exactly one line. Zero matches is the failure that shipped
# placeholders: a rewrite that matched nothing reported success and left the template's values in
# place, release after release. Two matches is a rewrite that changed a second line as well as the
# intended one. Both throw, naming the key. A key is found at any indentation and as a list item
# ("- Key:"), as a whole key only, never inside a comment; the value runs from the separator to the
# end of the line and is written literally, so a value holding regex characters lands as typed. The
# file's line ending and byte-order mark are kept, and a file that mixes endings is refused before
# it is rewritten.

Set-StrictMode -Version Latest

function Get-ManifestLineEnding([string]$Text) {
    # CRLF, LF, Mixed or None. A bare CR is not a line ending any manifest uses and is left alone.
    $crlf = [regex]::Matches($Text, "\r\n").Count
    $bareLf = [regex]::Matches($Text, "(?<!\r)\n").Count
    if ($crlf -gt 0 -and $bareLf -gt 0) { return "Mixed" }
    if ($crlf -gt 0) { return "CRLF" }
    if ($bareLf -gt 0) { return "LF" }
    return "None"
}

function Find-ManifestLine([string]$Path, [string]$Key) {
    if ([string]::IsNullOrWhiteSpace($Key)) { throw "A manifest key must be given." }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Manifest '$Path' does not exist." }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.UTF8Encoding]::new($false).GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    $ending = Get-ManifestLineEnding $text
    $lines = $text -split "\r?\n"

    # Whole key, any indentation, optionally a list item; a comment never matches because "#" is
    # not whitespace. The value is lazy so trailing whitespace stays out of it.
    $pattern = '^(?<lead>\s*(?:-\s+)?)' + [regex]::Escape($Key) + '(?<sep>\s*:\s*)(?<value>.*?)\s*$'
    $hits = @()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -cmatch $pattern) {
            $hits += [pscustomobject]@{
                Index     = $i
                Lead      = $Matches['lead']
                Separator = $Matches['sep']
                Value     = $Matches['value']
            }
        }
    }
    if ($hits.Count -eq 0) {
        throw "'$Key' matches no line in $Path. A rewrite that matched nothing would leave the template's value in place and report success; the key is absent or spelt otherwise."
    }
    if ($hits.Count -gt 1) {
        $where = ($hits | ForEach-Object { $_.Index + 1 }) -join ', '
        throw "'$Key' matches $($hits.Count) lines in $Path (lines $where); exactly one is required."
    }
    return [pscustomobject]@{
        Lines     = $lines
        Ending    = $ending
        Separator = $(if ($ending -eq "CRLF") { "`r`n" } else { "`n" })
        HasBom    = $hasBom
        Hit       = $hits[0]
    }
}

function Get-ManifestValue([string]$Path, [string]$Key) {
    return (Find-ManifestLine $Path $Key).Hit.Value
}

function Set-ManifestValue([string]$Path, [string]$Key, [string]$Value) {
    if ($null -eq $Value) { throw "A value for '$Key' must be given." }
    if ($Value -match "[\r\n]") { throw "The value for '$Key' holds a line break; a manifest value is one line." }

    $found = Find-ManifestLine $Path $Key
    if ($found.Ending -eq "Mixed") {
        throw "$Path mixes CRLF and LF line endings; it is not rewritten until it is consistent."
    }
    $hit = $found.Hit
    $separator = $hit.Separator
    if ($separator -notmatch '\s$') { $separator += ' ' }
    $found.Lines[$hit.Index] = $hit.Lead + $Key + $separator + $Value

    $encoding = [System.Text.UTF8Encoding]::new($found.HasBom)
    [System.IO.File]::WriteAllText($Path, ($found.Lines -join $found.Separator), $encoding)

    # Read back rather than trust the write: the file on disk is what ships.
    $after = Get-ManifestValue $Path $Key
    if ($after -cne $Value) {
        throw "After rewriting, '$Key' in $Path reads '$after', not '$Value'."
    }
}
