param(
    [switch]$SkipHidHideDownload,
    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "XboxGuideTray\XboxGuideTray.csproj"
$publishDir = Join-Path $repoRoot "XboxGuideTray\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$installerDir = $PSScriptRoot
$redistDir = Join-Path $installerDir "redist"
$issPath = Join-Path $installerDir "XboxGuideTray.iss"
$hidHideVersion = "1.5.230"
$hidHideFileName = "HidHide_${hidHideVersion}_x64.exe"
$hidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v${hidHideVersion}.0/$hidHideFileName"

function Find-InnoSetupCompiler {
    param([string]$PreferredPath)

    if ($PreferredPath -and (Test-Path $PreferredPath)) {
        return $PreferredPath
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php and rerun this script."
}

Write-Host "Publishing Xbox Guide Tray (self-contained win-x64)..."
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if (-not (Test-Path (Join-Path $publishDir "XboxGuideTray.exe"))) {
    throw "Publish output not found at $publishDir"
}

if (-not $SkipHidHideDownload) {
    New-Item -ItemType Directory -Force -Path $redistDir | Out-Null
    $hidHidePath = Join-Path $redistDir $hidHideFileName

    if (-not (Test-Path $hidHidePath)) {
        Write-Host "Downloading HidHide $hidHideVersion..."
        Invoke-WebRequest -Uri $hidHideUrl -OutFile $hidHidePath -UseBasicParsing
    }
    else {
        Write-Host "Using cached HidHide installer at $hidHidePath"
    }
}
else {
    Write-Host "Skipping HidHide download. Installer will fetch it during setup if needed."
}

$iscc = Find-InnoSetupCompiler -PreferredPath $InnoSetupPath
Write-Host "Compiling installer with $iscc ..."
& $iscc $issPath

$outputDir = Join-Path $installerDir "output"
$setup = Get-ChildItem $outputDir -Filter "XboxGuideTray-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $setup) {
    throw "Installer build completed but no setup executable was found in $outputDir"
}

Write-Host ""
Write-Host "Installer created:"
Write-Host "  $($setup.FullName)"
