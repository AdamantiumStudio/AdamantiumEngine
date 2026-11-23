<#
.SYNOPSIS
Folds the changelog entries waiting in changelog.d into CHANGELOG.md, under "## Unreleased", and deletes their files.

.DESCRIPTION
Each change writes its entry into a file of its own in changelog.d (see changelog.d/README.md), so changes made side by
side never touch the same lines. This puts them where they belong: each "### Section" of a file goes under the same
section of "## Unreleased" - made, in Keep a Changelog's order, when it is not there yet - before what that section
already holds, the files taken in the order of their names. Run it before a release, ahead of naming the version.

.PARAMETER Root
The repository's root; the folder this script is in by default.

.PARAMETER WhatIf
Shows CHANGELOG.md as it would be, and deletes nothing.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$Root = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$order = 'Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security'
$changelog = Join-Path $Root 'CHANGELOG.md'
$folder = Join-Path $Root 'changelog.d'

$files = @(Get-ChildItem -Path $folder -Filter '*.md' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'README.md' } | Sort-Object Name)
if ($files.Count -eq 0) {
    Write-Host 'No entries are waiting in changelog.d.'
    return
}

# The entries of every file, by section, in the order the files come.
$waiting = [ordered]@{}
foreach ($file in $files) {
    $section = $null
    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        if ($line -match '^###\s+(\S.*?)\s*$') {
            $section = $Matches[1]
            if ($order -notcontains $section) { throw "$($file.Name): '### $section' is not a section of Keep a Changelog ($($order -join ', '))." }
            if (-not $waiting.Contains($section)) { $waiting[$section] = [Collections.Generic.List[string]]::new() }
            continue
        }

        if ($null -eq $section) {
            if ($line.Trim()) { throw "$($file.Name): an entry comes before any '### Section' line." }
            continue
        }

        # The entries of a section stand one under another, as in CHANGELOG.md: blank lines are not carried over.
        if ($line.Trim()) { $waiting[$section].Add($line) }
    }
}

$bytes = [IO.File]::ReadAllBytes($changelog)
$bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$raw = [Text.Encoding]::UTF8.GetString($bytes)
$newline = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
$lines = [Collections.Generic.List[string]]::new([IO.File]::ReadAllLines($changelog))
$unreleased = $lines.IndexOf('## Unreleased')
if ($unreleased -lt 0) { throw "CHANGELOG.md has no '## Unreleased' heading." }
$end = $unreleased + 1
while ($end -lt $lines.Count -and -not $lines[$end].StartsWith('## ')) { $end++ }

foreach ($section in $waiting.Keys) {
    $entries = [Collections.Generic.List[string]]::new($waiting[$section])
    while ($entries.Count -gt 0 -and -not $entries[$entries.Count - 1].Trim()) { $entries.RemoveAt($entries.Count - 1) }

    $heading = -1
    for ($i = $unreleased + 1; $i -lt $end; $i++) {
        if ($lines[$i] -eq "### $section") { $heading = $i; break }
    }

    if ($heading -lt 0) {
        # Made where Keep a Changelog puts it: before the first section that comes after it in the order, else last.
        $at = $end
        $rank = [Array]::IndexOf($order, $section)
        for ($i = $unreleased + 1; $i -lt $end; $i++) {
            if ($lines[$i] -match '^###\s+(\S.*?)\s*$' -and [Array]::IndexOf($order, $Matches[1]) -gt $rank) { $at = $i; break }
        }

        $made = [Collections.Generic.List[string]]::new()
        $made.Add("### $section")
        $made.Add('')
        $made.AddRange($entries)
        $made.Add('')
        $lines.InsertRange($at, $made)
        $end += $made.Count
        continue
    }

    # Under the heading and the blank line after it, before what the section already holds.
    $at = $heading + 1
    while ($at -lt $end -and -not $lines[$at].Trim()) { $at++ }
    $lines.InsertRange($at, $entries)
    $end += $entries.Count
}

if ($PSCmdlet.ShouldProcess($changelog, "fold in the entries of $($files.Count) files from changelog.d")) {
    # Written as it was read: its line ends, and its byte order mark or none.
    $text = ($lines -join $newline) + $newline
    [IO.File]::WriteAllText($changelog, $text, [Text.UTF8Encoding]::new($bom))
    $files | Remove-Item
    Write-Host "Folded the entries of $($files.Count) files into CHANGELOG.md."
}
else {
    $lines
}
