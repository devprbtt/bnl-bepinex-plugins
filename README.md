# BNL BepInEx Plugins

BepInEx 5 plugins for Block N Load (Unity 5.1.4). Replaces the old IL-patcher launcher approach with runtime BepInEx/Harmony mods.

> **Status**: Working — connects to community servers, EAC bypassed.

## Plugins

| Component | Layer | Description |
|-----------|-------|-------------|
| `BnlPlugins.Launcher` | Plugin | Harmony launcher parity patches, `servers.txt`, runtime shop image overrides |

## How it works

All patches run at runtime through BepInEx/Harmony — **no game files or game assemblies are modified on disk**, so Steam never triggers file verification. You can launch through Steam's Play button or directly.

```
Launch → BepInEx preloader
       → Plugins load (Harmony launcher patches + image overrides)
       → Game connects to community server
```

## Card Image Overrides

`BnlPlugins.Launcher` can override perk/shop card images at runtime from:

`BepInEx/plugins/Launcher/CardTextures/`

There is no `texture-map.txt`. The launcher indexes image files in that folder and matches them by filename.

Use the in-game shop image id as the filename, for example:

```
shop_perk_def_last_stand_momentum.png
shop_perk_off_heal_bane.png
shop_perk_hero_eliza_beautiful_bubbles.png
```

Supported image formats:

- `.png`
- `.jpg`
- `.jpeg`

Notes:

- The filename should match the requested card/shop sprite id.
- The launcher also tries simplified fallback names by stripping `shop_` and `shop_item_`.
- `tilesheet2.base.png` and `tilesheet3.base.png` may still exist in `Launcher/CardTextures/` from older experiments, but they are not required for perk/shop card overrides.

## Requirements

- **BepInEx 5.4.23** (x64) — the `bepinex-dist/` folder contains a pre-configured copy
- **.NET SDK** (any recent version) to build
- Block N Load installed via Steam

## Building

Set `GameDir` to your Block N Load install by creating `Directory.Build.props.user` in the repo root (gitignored):

```xml
<Project>
  <PropertyGroup>
    <GameManagedDir>C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\Win64\BlockNLoad_Data\Managed</GameManagedDir>
    <UnityManagedDir>C:\Program Files (x86)\Steam\steamapps\common\BlockNLoad\Win64\BlockNLoad_Data\Managed</UnityManagedDir>
  </PropertyGroup>
</Project>
```

Then build:

```
dotnet build BnlPlugins.Launcher
```

## Installing BepInEx 5 into Block N Load

### Quick Setup (using pre-configured dist)

Copy the entire contents of `bepinex-dist/` into your Block N Load game folder,
next to `BlockNLoad.exe`. The expected layout:

```
BlockNLoad\
├── BlockNLoad.exe
├── winhttp.dll              ← Doorstop proxy
├── doorstop_config.ini
├── .doorstop_version
└── BepInEx\
    ├── core\
    │   ├── BepInEx.dll
    │   ├── 0Harmony.dll
    │   └── Mono.Cecil.dll
    ├── config\
    │   └── BepInEx.cfg      ← Pre-configured for Unity 5
    └── plugins\
        ├── BnlPlugins.Launcher.dll
        └── Launcher\
            └── CardTextures\     ← Optional runtime card image overrides
```

After copying, place the built plugin DLLs into `BepInEx/plugins/`.

### Manual Setup

1. Download [BepInEx 5.4.23 win_x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
2. Extract into your Block N Load game folder (next to `BlockNLoad.exe`)
3. Replace `BepInEx/config/BepInEx.cfg` with the one from `bepinex-dist/BepInEx/config/BepInEx.cfg` in this repo (it has the Unity 5 fixes pre-applied)
4. Copy the built plugin DLLs into `BepInEx/plugins/`

## Unity 5 & Older Troubleshooting

Block N Load runs on Unity 5 (Mono runtime, .NET 3.5). BepInEx 5 needs these
specific settings to work correctly:

### 1. Entry Point (Critical)

The default BepInEx entry point is too early for Unity 5 games. The config
in this repo sets:

```ini
[Preloader.Entrypoint]
Assembly = UnityEngine.dll
Type = Camera
Method = .cctor
```

If the game shows a black screen on launch, try changing `Type` to `MonoBehaviour`.

Reference: [Discussion #377](https://github.com/BepInEx/BepInEx/discussions/377)

### 2. Harmony Backend (Critical)

Older Mono runtimes have an incomplete `System.Reflection.Emit`, which causes
`NotImplementedException` when Harmony tries to patch methods. The fix:

```ini
[Preloader]
HarmonyBackend = cecil
```

### 3. Console Logging (Debugging)

Enable the console to see BepInEx load progress and errors:

```ini
[Logging.Console]
Enabled = true
PreventClose = true
```

### 4. winhttp.dll vs version.dll

- **Unity 5**: `winhttp.dll` (default) should work fine
- **Unity 4 and older**: Rename `winhttp.dll` to `version.dll`

If the game crashes immediately on launch, try renaming the proxy DLL.

### 5. Missing System.Core.dll

If BepInEx fails to load with errors about missing assemblies, ensure
`System.Core.dll` is present in `BlockNLoad_Data\Managed\`. Unity 5 games
should include it by default, but if it's missing, copy it from a Unity 5
editor installation.

### Verification

After running the game with BepInEx installed, check for:

- **Console window** appearing alongside the game (shows BepInEx loading)
- **`BepInEx/LogOutput.log`** — should show plugin loading messages
- **`BepInEx/config/BnlPlugins.Launcher.cfg`** — launcher config file
- **`BepInEx/plugins/Launcher/CardTextures/`** — optional card/shop override images

## Configuration

After the first run with the plugins loaded, config files are generated at:

```
BepInEx/config/BnlPlugins.Launcher.cfg
```

Edit it to change server settings.
