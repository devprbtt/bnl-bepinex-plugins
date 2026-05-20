# Build all plugins and create a release zip ready for distribution.
# The zip mirrors the game folder structure — user just extracts to
# their BlockNLoad folder and launches through Steam.
param(
    [string]$Version = "1.1.0",
    [string]$OutputDir = ".\release"
)

$ErrorActionPreference = "Stop"
$workspace = $PSScriptRoot

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

# Copy card override images
$cardDir = "$workspace\bepinex-dist\BepInEx\plugins\Launcher\CardTextures"
if (Test-Path $cardDir) {
    Copy-Item "$cardDir\*" (Join-Path $staging "Win64\BepInEx\plugins\Launcher\CardTextures") -Exclude "*.base.png"
}

# Create zip (include the Win64 folder itself so extracting at game root places files correctly)
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging "Win64") -DestinationPath $zipPath

# Cleanup staging
Remove-Item $staging -Recurse -Force

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "=== Done: $zipPath ($zipSize MB) ==="
Write-Host ""
Write-Host "To install: extract this zip to your BlockNLoad folder."
Write-Host "  Steam:  C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad"
Write-Host "  Then launch the game through Steam as normal."
