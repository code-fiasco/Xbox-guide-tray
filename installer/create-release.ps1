# Creates GitHub release v1.0.0 with the installer asset.
# Requires: gh auth login

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $PSScriptRoot "output\XboxGuideTray-Setup-1.0.0.exe"
$notes = Join-Path $PSScriptRoot "release-notes-v1.0.0.md"

if (-not (Test-Path $installer)) {
    Write-Error "Installer not found. Run build-installer.ps1 first."
}

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
$ghExe = if ($ghCommand) { $ghCommand.Source } else { $null }
if (-not $ghExe) {
    $candidates = @(
        "${env:ProgramFiles}\GitHub CLI\gh.exe",
        "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe",
        "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
    )
    $ghExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $ghExe) {
    Write-Error "GitHub CLI (gh) is not installed. Install with: winget install GitHub.cli"
}

& $ghExe auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "GitHub CLI is not authenticated. Run: `"$ghExe`" auth login"
}

$repo = "code-fiasco/Xbox-guide-tray"

$ErrorActionPreference = 'Continue'
& $ghExe release view v1.0.0 --repo $repo --json tagName 2>&1 | Out-Null
$releaseExists = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = 'Stop'

if ($releaseExists) {
    Write-Host "Release v1.0.0 already exists. Uploading/replacing installer asset..."
    & $ghExe release upload v1.0.0 $installer --repo $repo --clobber
} else {
    & $ghExe release create v1.0.0 $installer `
        --repo $repo `
        --title "v1.0.0" `
        --notes-file $notes
}

Write-Host "Release published: https://github.com/code-fiasco/Xbox-guide-tray/releases/tag/v1.0.0"
