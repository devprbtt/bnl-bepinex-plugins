# BNL Community Launcher

One-click mod for Block N Load that connects you to the community server — no more "No servers available" screen. Safe for Steam, no game files modified.

> **Status**: Working — community servers online, EAC bypassed.

## Optional Plugins

In addition to the core launcher, the installer offers optional quality-of-life plugins:

| Plugin | Description |
|--------|-------------|
| **Aim Healthbar** | Show a healthbar for the unit your crosshair is aimed at. |
| **Auto Crouch** | Disable the forced-crouch behaviour when the ceiling is too low to stand. |
| **Auto Queue** | Auto-join casual queue from custom games; leaves when a match is found. |
| **Build Preview** | Optimistic local block placement with rollback on server rejection (recommended for high ping). |
| **Combat Numbers** | Damage, crit, healing, combine, and self-heal number controls. |
| **Crosshair** | Custom crosshair color, size, spread, shape, and ADS visibility. |
| **Death Cam HP** | Show spectated target HP in the death-cam nickname row; keep friendly healthbars visible while dead. |
| **FOV / ADS** | Override camera FOV, ADS sensitivity multiplier, and weapon model FOV. |
| **Impact VFX** | Hide impact/explosion VFX, lava/water plane visuals, and falling block visuals. |
| **Low HP Alert** | Highlight low-health friendlies with a configurable color and optional off-screen direction indicator. |
| **Map Render** | Override the map's environmental lighting preset (Default, Daytime, Sunset, Night). |
| **Misc** | Skip intro, disable main-menu frame cap, hide objective beam. |
| **Shield Timer** | Enemy shield buff bar with a circle or numeric duration timer. |
| **Team Colors** | Override friendly, enemy, and background team colors with presets or custom hex values. |
| **Teammate HP** | Show each teammate's HP percentage next to their name in the team panel. |
| **Unit GUI / WSI Scale** | Scale unit GUI elements and world-space indicators independently. |

These are opt-in — uncheck them in the installer if you don't want them.

## Installation

### Option 1: Automatic Installer (Recommended)

1. **Download** `BNL-Installer.exe` from the [latest release](https://github.com/devprbtt/bnl-bepinex-plugins/releases/latest)
2. **Double-click** to run — no dependencies, works on any Windows 10/11 PC
3. The installer auto-detects your Block N Load folder, lets you pick components, and installs everything
4. Optional: enable the Steam launch-options checkbox if you want the installer to write the direct `BlockNLoad.exe` launch option for your Steam user
5. If a release includes extra optional plugins, the installer shows only the ones you do not already have installed

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

You can also change these settings in-game: press **Home** to toggle the Configuration Manager.

## Configuration Manager

Press **Home** in-game to toggle a settings menu where you can change plugin settings without editing config files:

- Server host and port
- Settings for every optional plugin that is installed (Crosshair, FOV, Combat Numbers, Team Colors, Shield Timer, and more)

This is powered by [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) — included automatically.

The default hotkey is **Home**.

## Combat Numbers Plugin

When installed, the Combat Numbers plugin lets you customize how damage and healing numbers appear:

- **General**
  - **Enabled** — turn combat number overrides on or off.
  - **Alpha** — overall opacity for all damage and healing numbers.
- **Damage Numbers**
  - **Damage Color / Crit Color** — colors for normal and critical hits.
  - **Size Multiplier** — scale damage number size.
  - **Combine Damage** — accumulate repeated hits on the same target into one fading number.
  - **Show All Damage** — show damage numbers from all sources instead of only your own hits.
- **Healing Numbers**
  - **Heal Color** — color for healing numbers.
  - **Size Multiplier** — scale healing number size.
  - **Minimum Heal** — smallest heal amount that should produce a number.
  - **Show Friendly Healing / Show Self Healing** — control which healing numbers appear.
  - **Combine Healing** — accumulate rapid heals into one fading number.
- **Self Heal**
  - **Size Multiplier** — scale self-heal numbers separately.
  - **Offset X / Offset Y** — move self-heal numbers on screen.

Configure in-game via the Configuration Manager (press **Home**), or edit `BepInEx\config\bnl.community.combatnumbers.cfg`.

## Crosshair Plugin

Customise the in-game crosshair via the Configuration Manager:

- **Color** — idle, full-damage range, and below-max range colors. Hex input (`#RRGGBBAA`) with preset buttons.
- **Brightness / Alpha** — multiplier and overall opacity.
- **Size / Spread** — scale and bloom multipliers.
- **Force Shape** — override the weapon's crosshair type (`Dot`, `Crosshair`, `BrokenCircle`, `Hashed`, `HashedCrosshair`, `Melee`, or `__DEFAULT__`).
- **Force Show in ADS** — keep the crosshair visible while aiming down sights.
- **Hide Crosshair** — hide it entirely.

## Team Colors Plugin

Override the four team color slots used by healthbars and HUD elements:

- **Presets** — Default, Classic, Beta, or Custom.
- **Friendly / Enemy / Background Friendly / Background Enemy** — individual hex color pickers.

## Shield Timer Plugin

Adds a visual timer to enemy shield buffs:

- **Shield Bar Color** — color of the shield duration bar.
- **Clock Style** — circle countdown or numeric text.
- **Size Multiplier** — scale the timer widget.

## Aim Healthbar Plugin

Shows a healthbar above the unit your crosshair is currently aimed at. Toggle on/off in the Configuration Manager.

## Death Cam HP Plugin

While spectating after death, appends the target's current HP percentage to their nickname and keeps friendly healthbars visible on screen.

## Auto Queue Plugin

When enabled and you are in a custom game lobby, automatically enters casual matchmaking queue. Leaves the custom game as soon as a casual match is found.

## Build Preview Plugin

Applies block and device placements locally before the server confirms them. If the server rejects a placement it is rolled back. Most useful on high-latency connections.

## Low HP Alert Plugin

Highlights low-health friendly units:

- **Threshold** — HP percentage below which the alert activates.
- **Alert Color** — color applied to the healthbar.
- **Direction Indicator** — optional off-screen arrow pointing toward the unit.

## Teammate HP Plugin

Appends each teammate's current HP percentage to their name in the in-game team panel while they are alive.

## Impact VFX Plugin

Individually toggle:

- **Hide Impact VFX** — remove bullet/explosion hit particles.
- **Hide Lava / Water Plane** — remove lava and water surface visuals.
- **Hide Falling Blocks** — remove falling block physics objects.

## Unit GUI / WSI Scale Plugin

- **Unit GUI Scale** — multiplier for in-world unit UI (healthbars, names).
- **WSI Scale** — multiplier for world-space indicators.
Both have independent enable toggles.

## Map Render Plugin

Select a global lighting preset for the map environment: **Default**, **Daytime Warm**, **Daytime Cold**, **Sunset**, or **Night**.

## Misc Plugin

- **Skip Intro** — bypass the intro video on launch.
- **Disable Main-Menu Frame Cap** — remove the FPS cap on the main menu.
- **Hide Objective Beam** — hide the tall colored beam marking objectives.

## Auto Crouch Plugin

Disables the automatic crouch that the game forces when the player's head is too close to a ceiling.

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
BlockNLoad\Win64\BepInEx\plugins\BnlPlugins.Launcher\
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
