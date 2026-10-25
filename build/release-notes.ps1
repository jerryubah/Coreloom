<#
.SYNOPSIS
    Extracts the release notes for one version from CHANGELOG.md (Keep a Changelog format).

.EXAMPLE
    ./build/release-notes.ps1 -Version 1.2.0 -OutFile artifacts/release-notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$lines = Get-Content (Join-Path $Root 'CHANGELOG.md')

$collecting = $false
$notes = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines) {
    if ($line -match '^##\s+\[?v?([^\]\s]+)\]?') {
        if ($collecting) { break }
        if ($Matches[1] -eq $Version) { $collecting = $true; continue }
    }
    elseif ($collecting) {
        $notes.Add($line)
    }
}

$body = ($notes -join "`n").Trim()
if (-not $body) {
    Write-Warning "CHANGELOG.md has no section for $Version; using a generic note."
    $body = "Coreloom $Version."
}

$body += @"


---
**Mac** (macOS 14 Sonoma or later): ``Coreloom-macOS-Apple-Silicon.dmg`` for Apple silicon (M1 and later) or ``Coreloom-macOS-Intel.dmg`` for Intel. Open it and drag Coreloom into Applications. The first time you open it, macOS may say it could not verify Coreloom: open **System Settings › Privacy & Security** and click **Open Anyway** (only once). The ``-macos-*.zip`` files are what Coreloom's updater downloads.
**Windows** (Windows 11, 64-bit): ``Coreloom-Windows-Setup.exe``, the same installer as ``Coreloom-$Version-win-x64-Setup.exe``: per-user install (no administrator rights needed to install), updates in place from inside Coreloom; Coreloom itself always runs as administrator, so Windows asks for approval when you start it by hand. **Portable zip**: unzip anywhere and run ``Coreloom.exe``; settings stay in the ``Data`` folder next to it.
**Verify your download:** compare the file's SHA-256 with ``SHA256SUMS.txt`` (Mac: ``shasum -a 256 <file>``; PowerShell: ``Get-FileHash .\<file> -Algorithm SHA256``). The in-app updaters do this automatically.
"@

$dir = Split-Path -Parent $OutFile
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $body + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Release notes for $Version written to $OutFile"
