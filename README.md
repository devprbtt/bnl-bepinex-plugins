# BNL BepInEx Plugins

BepInEx 5 plugins and preloader patcher for Block N Load (Unity 5.1.4). Replaces the old IL-patcher launcher approach with proper BepInEx 5 modding.

> **Status**: Working — connects to community servers, EAC bypassed, FOV mod active.

## Plugins

| Component | Layer | Description |
|-----------|-------|-------------|
| `BnlPlugins.Patcher` | Preloader patcher | In-memory binary patches: EAC init NOP, servers.txt support, new player skip |
| `BnlPlugins.Launcher` | Plugin | Harmony: `IsEACRuntime→true`, writes `servers.txt` |
| `BnlPlugins.Fov` | Plugin | Camera FOV, weapon model FOV |

## How it works

All patches run in memory — **no game files are modified on disk**, so Steam never triggers file verification. You can launch through Steam's Play button or directly.

```
Launch → BepInEx preloader → Patcher swaps Assembly-CSharp in memory (patched)
       → Unity loads patched assembly
       → Plugins load (Harmony EAC bypass + FOV)
       → Game connects to community server
```

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
dotnet build BnlPlugins.Fov
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
    ├── plugins\              ← Put BnlPlugins.Fov.dll here
    └── patchers\
```

After copying, place the built `BnlPlugins.Fov.dll` into `BepInEx/plugins/`.

### Manual Setup

1. Download [BepInEx 5.4.23 win_x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
2. Extract into your Block N Load game folder (next to `BlockNLoad.exe`)
3. Replace `BepInEx/config/BepInEx.cfg` with the one from `bepinex-dist/BepInEx/config/BepInEx.cfg` in this repo (it has the Unity 5 fixes pre-applied)
4. Copy the built `BnlPlugins.Fov.dll` into `BepInEx/plugins/`

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
- **`BepInEx/config/bnl.community.fov.cfg`** — auto-generated config file

## Configuration

After the first run with the plugin loaded, a config file is generated at:

```
BepInEx/config/bnl.community.fov.cfg
```

Edit it to change FOV, weapon model FOV, and ADS sensitivity:
