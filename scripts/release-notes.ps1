<#
  Called by build.cmd after a successful build (as vShell's).
  Takes the version from vXplorer.csproj, finds its section in CHANGELOG.md ("## 0.1.21") and writes dist\release-notes.md -
  the update site's latest.json carries them.
#>
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$csproj = Join-Path $Root 'vXplorer.csproj'
$log    = Join-Path $Root 'CHANGELOG.md'
$out    = Join-Path $Root 'dist\release-notes.md'

$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Host 'No <Version> in vXplorer.csproj' -ForegroundColor Yellow; exit 0 }
$version = "$version".Trim()

$notes = @()
if (Test-Path $log) {
    $inside = $false
    # read as UTF-8: CHANGELOG.md has no BOM, and Windows PowerShell would read it as ANSI
    foreach ($line in [IO.File]::ReadAllLines($log, [Text.Encoding]::UTF8)) {
        if ($line -match '^##\s+v?([0-9][0-9.]*)') {
            if ($inside) { break }
            $inside = ($Matches[1] -eq $version)
            continue
        }
        if ($inside) { $notes += $line }
    }
}
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[0])) { $notes = @($notes | Select-Object -Skip 1) }
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[-1])) { $notes = @($notes | Select-Object -SkipLast 1) }

if ($notes.Count -eq 0) {
    $notes = @("(No notes for $version in CHANGELOG.md yet.)")
    Write-Host "CHANGELOG.md has no section for $version" -ForegroundColor Yellow
}
[IO.File]::WriteAllLines($out, [string[]]$notes, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host "Release notes: $out (they go into the update site's latest.json)"
