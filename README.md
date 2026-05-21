# BNL Community Launcher

One-click mod for Block N Load that connects you to the community server — no more "No servers available" screen. Safe for Steam, no game files modified.

> **Status**: Working — community servers online, EAC bypassed.

## Optional Plugins

In addition to the core launcher, the installer offers optional quality-of-life plugins:

| Plugin | Description |
|--------|-------------|
| **Crosshair** | Custom crosshair color, size, spread, shape, and ADS visibility. Configure in-game via the Configuration Manager (press **`**). |
| **FOV / ADS** | Override camera FOV, ADS sensitivity multiplier, and weapon model FOV. |

These are opt-in — uncheck them in the installer if you don't want them.

## Installation

### Option 1: Automatic Installer (Recommended)

1. **Download** `BNL-Installer.exe` from the [latest release](https://github.com/devprbtt/bnl-bepinex-plugins/releases/latest)
2. **Double-click** to run — no dependencies, works on any Windows 10/11 PC
3. The installer auto-detects your Block N Load folder, lets you pick components, and installs everything
4. Optional: enable the Steam launch-options checkbox if you want the installer to write the direct `BlockNLoad.exe` launch option for your Steam user
5. If a release includes extra optional plugins, the installer will show them before extraction so you can skip anything you do not want

### Option 2: Manual Install

1. **Download** `bnl-bepinex-plugins-vX.X.X.zip` from [Releases](https://github.com/devprbtt/bnl-bepinex-plugins/releases)
2. **Extract** the zip into your Block N Load game folder:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\
   ```
3. **Launch** Block N Load through Steam as normal

That's it. You'll see a console window appear alongside the game — that's BepInEx loading the mod. The game will connect to the community server automatically.

## Card Image Overrides

Want custom perk/shop card images? Drop `.png` files into:

```
BlockNLoad\Win64\BepInEx\plugins\Launcher\CardTextures\
```

Name them after the shop image ID, for example:

```
shop_perk_def_last_stand_momentum.png
shop_perk_off_heal_bane.png
shop_perk_hero_eliza_beautiful_bubbles.png
```

Supported formats: `.png`, `.jpg`, `.jpeg`

The launcher indexes files by filename and matches them when the game requests a sprite. It also tries fallback names by stripping `shop_` and `shop_item_` prefixes.

## Server Configuration

After your first launch, edit:

```
BlockNLoad\Win64\BepInEx\config\BnlPlugins.Launcher.cfg
```

```ini
[Server]
host=v310.blocknload.pauldh.nl
port=28100
```

You can also change these settings in-game: press **`** to open the Configuration Manager.

## Configuration Manager

Press **`** in-game to open a settings menu where you can change plugin settings without editing config files:

- Server host and port
- Crosshair color, size, spread, shape, and ADS visibility (if Crosshair plugin installed)
- FOV and ADS sensitivity (if FOV plugin installed)

This is powered by [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) — included automatically.

The default hotkey is **`**.

## Crosshair Plugin

When installed, the Crosshair plugin lets you customize the in-game crosshair via the Configuration Manager:

- **Color** — separate colors for idle, full-damage range, and below-max range targets. Supports hex input (`#RRGGBBAA`) and preset color buttons.
- **Brightness** — multiplier applied on top of the color.
- **Alpha** — overall crosshair transparency.
- **Size** — scale multiplier for the crosshair widget.
- **Spread** — multiplier for the bloom/spread angle.
- **Force Shape** — override the weapon's crosshair type (`Dot`, `Crosshair`, `BrokenCircle`, `Hashed`, `HashedCrosshair`, `Melee`, or `__DEFAULT__` to leave it as-is).
- **Force Show in ADS** — keep the crosshair visible while aiming down sights.
- **Hide Crosshair** — hide it entirely.

## Auto-Updates

The launcher checks for updates automatically when you start the game, but the actual update logic is handled by the local installer instead of the game itself.

- If you're already up to date, nothing opens.
- If an update is available, `BNL-Installer.exe` opens and lets you choose which components to install.
- You can also force a manual check from the in-game Configuration Manager.

## Steam Launch Option

The installer can optionally write a Steam launch option for Block N Load:

```
"...\BlockNLoad\Win64\BlockNLoad.exe" %COMMAND%
```

This is off by default and must be selected in the installer. If you use it, close Steam before installing so the change is written to your Steam user config cleanly.

## Uninstalling

### Option 1: Automatic Uninstaller

1. **Download** `BNL-Uninstaller.exe` from the latest release
2. **Run** it
3. Pick what to remove:
   - launcher plugin
   - card override folder
   - launcher config/version files
   - optional Configuration Manager
   - optional BepInEx runtime and Doorstop files

By default it removes only the community launcher files, so other WIP plugins can stay installed.

### Option 2: Manual Removal

At minimum, delete:

```
BlockNLoad\Win64\BepInEx\plugins\BnlPlugins.Launcher.dll
BlockNLoad\Win64\BepInEx\plugins\Launcher\
BlockNLoad\Win64\BepInEx\config\BnlPlugins.Launcher.cfg
```

If you want to fully revert the loader too, also delete:

```
BlockNLoad\Win64\winhttp.dll
BlockNLoad\Win64\.doorstop_version
BlockNLoad\Win64\doorstop_config.ini
BlockNLoad\Win64\changelog.txt
BlockNLoad\Win64\BepInEx\
```

---

## For Developers

### Requirements

- .NET SDK (any recent version)
- Block N Load installed via Steam

### Building

Create `Directory.Build.props.user` in the repo root (gitignored):

```xml
<Project>
  <PropertyGroup>
    <GameManagedDir>C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\Win64\BlockNLoad_Data\Managed</GameManagedDir>
    <UnityManagedDir>C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\Win64\BlockNLoad_Data\Managed</UnityManagedDir>
  </PropertyGroup>
</Project>
```

Build:

```
dotnet build BnlPlugins.Launcher
```

### Creating a Release Zip

```
.\pack-release.ps1 -Version "1.1.0"
```

Outputs to `.\release\bnl-bepinex-plugins-v1.1.0.zip`.

### How it works

All patches run at runtime through BepInEx/Harmony — **no game files are modified on disk**, so Steam never triggers verification.

```
Launch → BepInEx preloader loads
       → Launcher plugin applies Harmony patches (EAC bypass, server override)
       → Game connects to community server
```

## Troubleshooting

Block N Load runs on Unity 5 (Mono runtime, .NET 3.5). The included BepInEx config has these fixes pre-applied:

### Entry Point

```ini
[Preloader.Entrypoint]
Assembly = UnityEngine.dll
Type = Camera
Method = .cctor
```

If the game shows a black screen, try changing `Type` to `MonoBehaviour`.

### Harmony Backend

```ini
[Preloader]
HarmonyBackend = cecil
```

Required for older Mono runtimes with incomplete `System.Reflection.Emit`.

### Verification

After installing, check for:

- **Console window** appearing alongside the game
- **`Win64\BepInEx\LogOutput.log`** — shows plugin loading messages
- **`Win64\BepInEx\config\BnlPlugins.Launcher.cfg`** — server config
