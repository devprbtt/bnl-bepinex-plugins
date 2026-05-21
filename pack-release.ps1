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

# Build installer exe
dotnet build "$workspace\BnlInstaller\BnlInstaller.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }

# Build uninstaller exe
dotnet build "$workspace\BnlUninstaller\BnlUninstaller.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Uninstaller build failed" }

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
Copy-Item "$workspace\bepinex-dist\BepInEx\config\com.bepis.bepinex.configurationmanager.cfg" (Join-Path $staging "Win64\BepInEx\config") -ErrorAction SilentlyContinue

# Copy built plugin DLL
Copy-Item "$workspace\BnlPlugins.Launcher\bin\Release\net35\BnlPlugins.Launcher.dll" (Join-Path $staging "Win64\BepInEx\plugins")
Copy-Item "$workspace\BnlInstaller\bin\Release\net472\BNL-Installer.exe" (Join-Path $staging "Win64\BepInEx\plugins\Launcher\BNL-Installer.exe")

# Copy Configuration Manager (in-game settings menu, F1)
$cfgManDir = "$workspace\bepinex-dist\BepInEx\plugins\ConfigurationManager"
if (Test-Path $cfgManDir) {
    Copy-Item $cfgManDir (Join-Path $staging "Win64\BepInEx\plugins\ConfigurationManager") -Recurse
    Write-Host "  Included Configuration Manager"
}

# Write version file (used by auto-update check)
$Version | Out-File -FilePath (Join-Path $staging "Win64\BepInEx\plugins\Launcher\version.txt") -Encoding ascii -NoNewline
Write-Host "  Version: $Version"

# Write release manifest (used by the installer to present optional components)
$manifestPath = Join-Path $staging "Win64\BepInEx\plugins\Launcher\release-manifest.json"
$manifest = [ordered]@{
    version = $Version
    components = @(
        [ordered]@{
            id = "bepinex-runtime"
            name = "BepInEx Runtime"
            description = "Doorstop bootstrap and BepInEx core files."
            required = $true
            default = $true
            paths = @(
                ".doorstop_version",
                "changelog.txt",
                "doorstop_config.ini",
                "winhttp.dll",
                "BepInEx/core/",
                "BepInEx/config/BepInEx.cfg"
            )
        },
        [ordered]@{
            id = "launcher"
            name = "Community Launcher"
            description = "Server patches, installer helper, and version metadata."
            required = $true
            default = $true
            paths = @(
                "BepInEx/plugins/BnlPlugins.Launcher.dll",
                "BepInEx/plugins/Launcher/BNL-Installer.exe",
                "BepInEx/plugins/Launcher/version.txt",
                "BepInEx/plugins/Launcher/release-manifest.json"
            )
        },
        [ordered]@{
            id = "card-textures"
            name = "Card Texture Overrides"
            description = "Bundled custom card images."
            required = $false
            default = $true
            paths = @(
                "BepInEx/plugins/Launcher/CardTextures/"
            )
        },
        [ordered]@{
            id = "configuration-manager"
            name = "Configuration Manager"
            description = "In-game settings UI."
            required = $false
            default = $true
            paths = @(
                "BepInEx/plugins/ConfigurationManager/",
                "BepInEx/config/com.bepis.bepinex.configurationmanager.cfg"
            )
        }
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8

# Copy card override images from workspace dist only.
# Releases must be reproducible and must not pick up personal overrides
# from the local game install.
$cardStaging = Join-Path $staging "Win64\BepInEx\plugins\Launcher\CardTextures"
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

# Copy installer exe to release folder
Copy-Item "$workspace\BnlInstaller\bin\Release\net472\BNL-Installer.exe" $OutputDir -Force
Copy-Item "$workspace\BnlUninstaller\bin\Release\net472\BNL-Uninstaller.exe" $OutputDir -Force

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "=== Done: $zipPath ($zipSize MB) ==="
Write-Host ""
Write-Host "Release assets ready in: $OutputDir"
Write-Host "  $zipName"
Write-Host "  BNL-Installer.exe"
Write-Host "  BNL-Uninstaller.exe"
Write-Host ""
Write-Host "Upload both to GitHub Releases. Users can either:"
Write-Host "  1. Run BNL-Installer.exe (auto-detect + GUI, no dependencies)"
Write-Host "  2. Run BNL-Uninstaller.exe to remove launcher files"
Write-Host "  3. Extract the zip manually"
