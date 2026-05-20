# Build all plugins and create a release zip ready for distribution.
# The zip mirrors the game folder structure — user just extracts to
# their BlockNLoad folder and launches through Steam.
param(
    [string]$Version = "1.1.0",
    [string]$OutputDir = ".\release",
    [string]$GameRoot = ""
)

$ErrorActionPreference = "Stop"
$workspace = $PSScriptRoot

# Auto-detect game root from Directory.Build.props if not specified
if ([string]::IsNullOrEmpty($GameRoot)) {
    $propsPath = Join-Path $workspace "Directory.Build.props"
    if (Test-Path $propsPath) {
        $propsXml = [xml](Get-Content $propsPath)
        $managedDir = $propsXml.Project.PropertyGroup.GameManagedDir
        if ($managedDir) {
            # GameManagedDir is ...\Win64\BlockNLoad_Data\Managed → game root is 3 levels up
            $GameRoot = (Get-Item (Join-Path $managedDir "..\..\..")).FullName
        }
    }
}

if ([string]::IsNullOrEmpty($GameRoot) -or !(Test-Path $GameRoot)) {
    Write-Host "WARNING: Could not auto-detect game folder. Card textures will be minimal."
    Write-Host "  Pass -GameRoot 'C:\...\BlockNLoad' to include all card textures."
    $GameRoot = $null
}

Write-Host "=== Building plugins ==="

# Build Launcher (the only plugin included in releases)
dotnet build "$workspace\BnlPlugins.Launcher\BnlPlugins.Launcher.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed" }

Write-Host "=== Preparing release package ==="

$staging = Join-Path $OutputDir "staging"
$zipName = "bnl-bepinex-plugins-v$Version.zip"
$zipPath = Join-Path $OutputDir $zipName

# Clean staging
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $staging "Win64\BepInEx\config") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "Win64\BepInEx\core") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "Win64\BepInEx\plugins\Launcher\CardTextures") -Force | Out-Null

# Copy Doorstop files (go in Win64/)
Copy-Item "$workspace\bepinex-dist\.doorstop_version" (Join-Path $staging "Win64\") -ErrorAction SilentlyContinue
Copy-Item "$workspace\bepinex-dist\changelog.txt" (Join-Path $staging "Win64\")
Copy-Item "$workspace\bepinex-dist\doorstop_config.ini" (Join-Path $staging "Win64\")
Copy-Item "$workspace\bepinex-dist\winhttp.dll" (Join-Path $staging "Win64\")

# Copy BepInEx core
Copy-Item "$workspace\bepinex-dist\BepInEx\core\*" (Join-Path $staging "Win64\BepInEx\core") -Exclude "*.xml"

# Copy BepInEx config
Copy-Item "$workspace\bepinex-dist\BepInEx\config\BepInEx.cfg" (Join-Path $staging "Win64\BepInEx\config")

# Copy built plugin DLL
Copy-Item "$workspace\BnlPlugins.Launcher\bin\Release\net35\BnlPlugins.Launcher.dll" (Join-Path $staging "Win64\BepInEx\plugins")

# Copy card override images — pull from game folder (source of truth) first,
# then fill in any missing from workspace dist
$cardStaging = Join-Path $staging "Win64\BepInEx\plugins\Launcher\CardTextures"
if ($GameRoot) {
    $gameCardDir = "$GameRoot\Win64\BepInEx\plugins\Launcher\CardTextures"
    if (Test-Path $gameCardDir) {
        Copy-Item "$gameCardDir\*" $cardStaging -Exclude "*.base.png"
        Write-Host "  Copied card textures from game folder: $gameCardDir"
    }
}
# Fallback: workspace dist card textures
$distCardDir = "$workspace\bepinex-dist\BepInEx\plugins\Launcher\CardTextures"
if (Test-Path $distCardDir) {
    Copy-Item "$distCardDir\*" $cardStaging -Exclude "*.base.png"
    Write-Host "  Copied card textures from workspace dist: $distCardDir"
}

# Create zip (include the Win64 folder itself so extracting at game root places files correctly)
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging "Win64") -DestinationPath $zipPath

# Cleanup staging
Remove-Item $staging -Recurse -Force

# Copy installer script to release folder
Copy-Item "$workspace\install.ps1" $OutputDir -Force

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "=== Done: $zipPath ($zipSize MB) ==="
Write-Host ""
Write-Host "Release assets ready in: $OutputDir"
Write-Host "  $zipName"
Write-Host "  install.ps1"
Write-Host ""
Write-Host "Upload both to GitHub Releases. Users can either:"
Write-Host "  1. Run install.ps1 (auto-detect + GUI)"
Write-Host "  2. Extract the zip manually"
