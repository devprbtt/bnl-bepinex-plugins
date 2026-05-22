# Build all plugins and create a release zip ready for distribution.
# The zip mirrors the game folder structure — user just extracts to
# their BlockNLoad folder and launches through Steam.
param(
    [string]$Version = "1.4.4",
    [string]$OutputDir = ".\release",
    [string]$GameRoot = ""
)

$ErrorActionPreference = "Stop"
$workspace = $PSScriptRoot

function Get-DllVersion($path) {
    if (!(Test-Path $path)) { return $null }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($info.ProductVersion) { return ($info.ProductVersion -split '[ +]')[0] }
    if ($info.FileVersion) { return ($info.FileVersion -split '[ +]')[0] }
    return $null
}

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

$fovProject = "$workspace\BnlPlugins.Fov\BnlPlugins.Fov.csproj"
$fovDll = "$workspace\BnlPlugins.Fov\bin\Release\net35\BnlPlugins.Fov.dll"
if (Test-Path $fovProject) {
    dotnet build $fovProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "FOV plugin build failed" }
}

$crosshairProject = "$workspace\BnlPlugins.Crosshair\BnlPlugins.Crosshair.csproj"
$crosshairDll = "$workspace\BnlPlugins.Crosshair\bin\Release\net35\BnlPlugins.Crosshair.dll"
if (Test-Path $crosshairProject) {
    dotnet build $crosshairProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "Crosshair plugin build failed" }
}

$combatNumbersProject = "$workspace\BnlPlugins.CombatNumbers\BnlPlugins.CombatNumbers.csproj"
$combatNumbersDll = "$workspace\BnlPlugins.CombatNumbers\bin\Release\net35\BnlPlugins.CombatNumbers.dll"
if (Test-Path $combatNumbersProject) {
    dotnet build $combatNumbersProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "CombatNumbers plugin build failed" }
}

$shieldTimerProject = "$workspace\BnlPlugins.ShieldTimer\BnlPlugins.ShieldTimer.csproj"
$shieldTimerDll = "$workspace\BnlPlugins.ShieldTimer\bin\Release\net35\BnlPlugins.ShieldTimer.dll"
if (Test-Path $shieldTimerProject) {
    dotnet build $shieldTimerProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "ShieldTimer plugin build failed" }
}

$buildPreviewProject = "$workspace\BnlPlugins.BuildPreview\BnlPlugins.BuildPreview.csproj"
$buildPreviewDll = "$workspace\BnlPlugins.BuildPreview\bin\Release\net35\BnlPlugins.BuildPreview.dll"
if (Test-Path $buildPreviewProject) {
    dotnet build $buildPreviewProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "BuildPreview plugin build failed" }
}

$aimHealthbarProject = "$workspace\BnlPlugins.AimHealthbar\BnlPlugins.AimHealthbar.csproj"
$aimHealthbarDll = "$workspace\BnlPlugins.AimHealthbar\bin\Release\net35\BnlPlugins.AimHealthbar.dll"
if (Test-Path $aimHealthbarProject) {
    dotnet build $aimHealthbarProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "AimHealthbar plugin build failed" }
}

$deathCamHpProject = "$workspace\BnlPlugins.DeathCamHp\BnlPlugins.DeathCamHp.csproj"
$deathCamHpDll = "$workspace\BnlPlugins.DeathCamHp\bin\Release\net35\BnlPlugins.DeathCamHp.dll"
if (Test-Path $deathCamHpProject) {
    dotnet build $deathCamHpProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "DeathCamHp plugin build failed" }
}

$autoQueueProject = "$workspace\BnlPlugins.AutoQueue\BnlPlugins.AutoQueue.csproj"
$autoQueueDll = "$workspace\BnlPlugins.AutoQueue\bin\Release\net35\BnlPlugins.AutoQueue.dll"
if (Test-Path $autoQueueProject) {
    dotnet build $autoQueueProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "AutoQueue plugin build failed" }
}

$lowHpAlertProject = "$workspace\BnlPlugins.LowHpAlert\BnlPlugins.LowHpAlert.csproj"
$lowHpAlertDll = "$workspace\BnlPlugins.LowHpAlert\bin\Release\net35\BnlPlugins.LowHpAlert.dll"
if (Test-Path $lowHpAlertProject) {
    dotnet build $lowHpAlertProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "LowHpAlert plugin build failed" }
}

$autoCrouchProject = "$workspace\BnlPlugins.AutoCrouch\BnlPlugins.AutoCrouch.csproj"
$autoCrouchDll = "$workspace\BnlPlugins.AutoCrouch\bin\Release\net35\BnlPlugins.AutoCrouch.dll"
if (Test-Path $autoCrouchProject) {
    dotnet build $autoCrouchProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "AutoCrouch plugin build failed" }
}

$teammateHpProject = "$workspace\BnlPlugins.TeammateHp\BnlPlugins.TeammateHp.csproj"
$teammateHpDll = "$workspace\BnlPlugins.TeammateHp\bin\Release\net35\BnlPlugins.TeammateHp.dll"
if (Test-Path $teammateHpProject) {
    dotnet build $teammateHpProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "TeammateHp plugin build failed" }
}

$impactVfxProject = "$workspace\BnlPlugins.ImpactVfx\BnlPlugins.ImpactVfx.csproj"
$impactVfxDll = "$workspace\BnlPlugins.ImpactVfx\bin\Release\net35\BnlPlugins.ImpactVfx.dll"
if (Test-Path $impactVfxProject) {
    dotnet build $impactVfxProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "ImpactVfx plugin build failed" }
}

$unitGuiWsiScaleProject = "$workspace\BnlPlugins.UnitGuiWsiScale\BnlPlugins.UnitGuiWsiScale.csproj"
$unitGuiWsiScaleDll = "$workspace\BnlPlugins.UnitGuiWsiScale\bin\Release\net35\BnlPlugins.UnitGuiWsiScale.dll"
if (Test-Path $unitGuiWsiScaleProject) {
    dotnet build $unitGuiWsiScaleProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "UnitGuiWsiScale plugin build failed" }
}

$mapRenderProject = "$workspace\BnlPlugins.MapRender\BnlPlugins.MapRender.csproj"
$mapRenderDll = "$workspace\BnlPlugins.MapRender\bin\Release\net35\BnlPlugins.MapRender.dll"
if (Test-Path $mapRenderProject) {
    dotnet build $mapRenderProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "MapRender plugin build failed" }
}

$miscProject = "$workspace\BnlPlugins.Misc\BnlPlugins.Misc.csproj"
$miscDll = "$workspace\BnlPlugins.Misc\bin\Release\net35\BnlPlugins.Misc.dll"
if (Test-Path $miscProject) {
    dotnet build $miscProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "Misc plugin build failed" }
}

$teamColorsProject = "$workspace\BnlPlugins.TeamColors\BnlPlugins.TeamColors.csproj"
$teamColorsDll = "$workspace\BnlPlugins.TeamColors\bin\Release\net35\BnlPlugins.TeamColors.dll"
if (Test-Path $teamColorsProject) {
    dotnet build $teamColorsProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "TeamColors plugin build failed" }
}

$launcherDll = "$workspace\BnlPlugins.Launcher\bin\Release\net35\BnlPlugins.Launcher.dll"
$launcherVersion = Get-DllVersion $launcherDll
$fovVersion = Get-DllVersion $fovDll
$crosshairVersion = Get-DllVersion $crosshairDll
$combatNumbersVersion = Get-DllVersion $combatNumbersDll
$shieldTimerVersion = Get-DllVersion $shieldTimerDll
$buildPreviewVersion = Get-DllVersion $buildPreviewDll
$aimHealthbarVersion = Get-DllVersion $aimHealthbarDll
$deathCamHpVersion = Get-DllVersion $deathCamHpDll
$autoQueueVersion = Get-DllVersion $autoQueueDll
$lowHpAlertVersion = Get-DllVersion $lowHpAlertDll
$autoCrouchVersion = Get-DllVersion $autoCrouchDll
$teammateHpVersion = Get-DllVersion $teammateHpDll
$impactVfxVersion = Get-DllVersion $impactVfxDll
$unitGuiWsiScaleVersion = Get-DllVersion $unitGuiWsiScaleDll
$mapRenderVersion = Get-DllVersion $mapRenderDll
$miscVersion = Get-DllVersion $miscDll
$teamColorsVersion = Get-DllVersion $teamColorsDll

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
New-Item -ItemType Directory -Path (Join-Path $staging "Win64\BepInEx\plugins\BnlPlugins.Launcher") -Force | Out-Null
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

# Copy built plugin DLLs — each into its own subfolder
Copy-Item "$workspace\BnlPlugins.Launcher\bin\Release\net35\BnlPlugins.Launcher.dll" (Join-Path $staging "Win64\BepInEx\plugins\BnlPlugins.Launcher")
Copy-Item "$workspace\BnlInstaller\bin\Release\net472\BNL-Installer.exe" (Join-Path $staging "Win64\BepInEx\plugins\Launcher\BNL-Installer.exe")

$optionalPlugins = @(
    @{ Dll = $fovDll;            Folder = "BnlPlugins.Fov" },
    @{ Dll = $crosshairDll;      Folder = "BnlPlugins.Crosshair" },
    @{ Dll = $combatNumbersDll;  Folder = "BnlPlugins.CombatNumbers" },
    @{ Dll = $shieldTimerDll;    Folder = "BnlPlugins.ShieldTimer" },
    @{ Dll = $buildPreviewDll;   Folder = "BnlPlugins.BuildPreview" },
    @{ Dll = $aimHealthbarDll;   Folder = "BnlPlugins.AimHealthbar" },
    @{ Dll = $deathCamHpDll;     Folder = "BnlPlugins.DeathCamHp" },
    @{ Dll = $autoQueueDll;      Folder = "BnlPlugins.AutoQueue" },
    @{ Dll = $lowHpAlertDll;     Folder = "BnlPlugins.LowHpAlert" },
    @{ Dll = $autoCrouchDll;     Folder = "BnlPlugins.AutoCrouch" },
    @{ Dll = $teammateHpDll;     Folder = "BnlPlugins.TeammateHp" },
    @{ Dll = $impactVfxDll;      Folder = "BnlPlugins.ImpactVfx" },
    @{ Dll = $unitGuiWsiScaleDll; Folder = "BnlPlugins.UnitGuiWsiScale" },
    @{ Dll = $mapRenderDll;      Folder = "BnlPlugins.MapRender" },
    @{ Dll = $miscDll;           Folder = "BnlPlugins.Misc" },
    @{ Dll = $teamColorsDll;     Folder = "BnlPlugins.TeamColors" }
)
foreach ($p in $optionalPlugins) {
    if (Test-Path $p.Dll) {
        $dest = Join-Path $staging "Win64\BepInEx\plugins\$($p.Folder)"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item $p.Dll $dest
    }
}

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
$manifestComponents = @(
    [ordered]@{
        id = "bepinex-runtime"
        name = "BepInEx Runtime"
        version = $Version
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
        version = $(if ($launcherVersion) { $launcherVersion } else { $Version })
        description = "Server patches, installer helper, and version metadata."
        required = $true
        default = $true
        paths = @(
            "BepInEx/plugins/BnlPlugins.Launcher/BnlPlugins.Launcher.dll",
            "BepInEx/plugins/Launcher/BNL-Installer.exe",
            "BepInEx/plugins/Launcher/version.txt",
            "BepInEx/plugins/Launcher/release-manifest.json"
        )
    },
    [ordered]@{
        id = "card-textures"
        name = "Card Texture Overrides"
        version = $Version
        description = "Bundled custom card images required by launcher-provided perk and shop overrides."
        required = $true
        default = $true
        paths = @(
            "BepInEx/plugins/Launcher/CardTextures/"
        )
    },
    [ordered]@{
        id = "configuration-manager"
        name = "Configuration Manager"
        version = "18.4.1"
        description = "In-game settings UI."
        required = $false
        default = $true
        paths = @(
            "BepInEx/plugins/ConfigurationManager/",
            "BepInEx/config/com.bepis.bepinex.configurationmanager.cfg"
        )
    }
)

if (Test-Path $fovDll) {
    $manifestComponents += [ordered]@{
        id = "fov"
        name = "FOV / ADS"
        version = $(if ($fovVersion) { $fovVersion } else { $Version })
        description = "Forced camera FOV, ADS sensitivity multiplier, and weapon model FOV."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.Fov/BnlPlugins.Fov.dll"
        )
    }
}

if (Test-Path $crosshairDll) {
    $manifestComponents += [ordered]@{
        id = "crosshair"
        name = "Crosshair"
        version = $(if ($crosshairVersion) { $crosshairVersion } else { $Version })
        description = "Crosshair color, size, spread, visibility, and forced-shape overrides."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.Crosshair/BnlPlugins.Crosshair.dll"
        )
    }
}

if (Test-Path $combatNumbersDll) {
    $manifestComponents += [ordered]@{
        id = "combatnumbers"
        name = "Combat Numbers"
        version = $(if ($combatNumbersVersion) { $combatNumbersVersion } else { $Version })
        description = "Damage, crit, healing, combine, and self-heal number controls that match the community launcher behavior."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.CombatNumbers/BnlPlugins.CombatNumbers.dll"
        )
    }
}

if (Test-Path $shieldTimerDll) {
    $manifestComponents += [ordered]@{
        id = "shieldtimer"
        name = "Shield Timer"
        version = $(if ($shieldTimerVersion) { $shieldTimerVersion } else { $Version })
        description = "Enemy shield buff bar with circle or numeric shield duration timer, matching the community launcher behavior."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.ShieldTimer/BnlPlugins.ShieldTimer.dll"
        )
    }
}

if (Test-Path $buildPreviewDll) {
    $manifestComponents += [ordered]@{
        id = "buildpreview"
        name = "Build Preview"
        version = $(if ($buildPreviewVersion) { $buildPreviewVersion } else { $Version })
        description = "Optimistic local block and device placement with rollback on server rejection. Recommended mainly for high ping."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.BuildPreview/BnlPlugins.BuildPreview.dll"
        )
    }
}

if (Test-Path $aimHealthbarDll) {
    $manifestComponents += [ordered]@{
        id = "aimhealthbar"
        name = "Aim Healthbar"
        version = $(if ($aimHealthbarVersion) { $aimHealthbarVersion } else { $Version })
        description = "Show a unit healthbar while your crosshair is aimed directly at that unit."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.AimHealthbar/BnlPlugins.AimHealthbar.dll"
        )
    }
}

if (Test-Path $deathCamHpDll) {
    $manifestComponents += [ordered]@{
        id = "deathcamhp"
        name = "Death Cam HP"
        version = $(if ($deathCamHpVersion) { $deathCamHpVersion } else { $Version })
        description = "Show spectated target HP in the death-cam nickname row and keep friendly healthbars visible while dead."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.DeathCamHp/BnlPlugins.DeathCamHp.dll"
        )
    }
}

if (Test-Path $autoQueueDll) {
    $manifestComponents += [ordered]@{
        id = "autoqueue"
        name = "Auto Queue"
        version = $(if ($autoQueueVersion) { $autoQueueVersion } else { $Version })
        description = "Automatically join casual queue from custom games and leave the custom game when a match is found."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.AutoQueue/BnlPlugins.AutoQueue.dll"
        )
    }
}

if (Test-Path $lowHpAlertDll) {
    $manifestComponents += [ordered]@{
        id = "lowhpalert"
        name = "Low HP Alert"
        version = $(if ($lowHpAlertVersion) { $lowHpAlertVersion } else { $Version })
        description = "Highlight low-health friendlies with an alert color and optional off-screen direction indicator."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.LowHpAlert/BnlPlugins.LowHpAlert.dll"
        )
    }
}

if (Test-Path $autoCrouchDll) {
    $manifestComponents += [ordered]@{
        id = "autocrouch"
        name = "Auto Crouch"
        version = $(if ($autoCrouchVersion) { $autoCrouchVersion } else { $Version })
        description = "Disable the forced-crouch behaviour that triggers when the ceiling is too low to stand."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.AutoCrouch/BnlPlugins.AutoCrouch.dll"
        )
    }
}

if (Test-Path $teammateHpDll) {
    $manifestComponents += [ordered]@{
        id = "teammatehp"
        name = "Teammate HP"
        version = $(if ($teammateHpVersion) { $teammateHpVersion } else { $Version })
        description = "Show each teammate's HP percentage next to their name in the team panel."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.TeammateHp/BnlPlugins.TeammateHp.dll"
        )
    }
}

if (Test-Path $impactVfxDll) {
    $manifestComponents += [ordered]@{
        id = "impactvfx"
        name = "Impact VFX"
        version = $(if ($impactVfxVersion) { $impactVfxVersion } else { $Version })
        description = "Hide impact and explosion VFX, lava/water plane visuals, and falling block visuals."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.ImpactVfx/BnlPlugins.ImpactVfx.dll"
        )
    }
}

if (Test-Path $unitGuiWsiScaleDll) {
    $manifestComponents += [ordered]@{
        id = "unitguiwsiscale"
        name = "Unit GUI / WSI Scale"
        version = $(if ($unitGuiWsiScaleVersion) { $unitGuiWsiScaleVersion } else { $Version })
        description = "Scale unit GUI elements and world-space indicators with separate toggles and multipliers."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.UnitGuiWsiScale/BnlPlugins.UnitGuiWsiScale.dll"
        )
    }
}

if (Test-Path $mapRenderDll) {
    $manifestComponents += [ordered]@{
        id = "maprender"
        name = "Map Render"
        version = $(if ($mapRenderVersion) { $mapRenderVersion } else { $Version })
        description = "Override the map's environmental lighting preset."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.MapRender/BnlPlugins.MapRender.dll"
        )
    }
}

if (Test-Path $teamColorsDll) {
    $manifestComponents += [ordered]@{
        id = "teamcolors"
        name = "Team Colors"
        version = $(if ($teamColorsVersion) { $teamColorsVersion } else { $Version })
        description = "Override friendly, enemy, and background team colors with presets or custom hex values."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.TeamColors/BnlPlugins.TeamColors.dll"
        )
    }
}

if (Test-Path $miscDll) {
    $manifestComponents += [ordered]@{
        id = "misc"
        name = "Misc"
        version = $(if ($miscVersion) { $miscVersion } else { $Version })
        description = "Skip intro, disable main-menu frame cap, and hide the objective beam."
        required = $false
        default = $false
        paths = @(
            "BepInEx/plugins/BnlPlugins.Misc/BnlPlugins.Misc.dll"
        )
    }
}

$manifest = [ordered]@{
    version = $Version
    components = $manifestComponents
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



