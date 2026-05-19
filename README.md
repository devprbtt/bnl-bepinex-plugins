# BNL BepInEx Plugins

BepInEx 5 plugins for Block N Load. This is a separate project from the main community launcher — the goal is to explore delivering features as proper BepInEx plugins rather than IL-patched DLLs.

## Status

Proof of concept. Currently contains:

| Plugin | Description |
|--------|-------------|
| `BnlPlugins.Fov` | Custom FOV, weapon model FOV, and ADS sensitivity multiplier |

## Requirements

- **BepInEx 5.4.23** installed into the Block N Load game directory
- **.NET SDK** (any recent version) to build

## Building

Set `GameDir` to your Block N Load install:

```
dotnet build BnlPlugins.Fov -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Block N Load"
```

Or create `Directory.Build.props.user` in the repo root (gitignored):

```xml
<Project>
  <PropertyGroup>
    <GameManagedDir>C:\Program Files (x86)\Steam\steamapps\common\Block N Load\BlockNLoad_Data\Managed</GameManagedDir>
    <UnityManagedDir>C:\Program Files (x86)\Steam\steamapps\common\Block N Load\BlockNLoad_Data\Managed</UnityManagedDir>
  </PropertyGroup>
</Project>
```

## Installing BepInEx 5

1. Download [BepInEx 5.4.23 win_x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
2. Extract into your Block N Load game folder (next to `BlockNLoad.exe`)
3. Run the game once to let BepInEx generate its folder structure
4. Copy the built `BnlPlugins.Fov.dll` into `BepInEx/plugins/`

## Configuration

After the first run with the plugin loaded, a config file is generated at:

```
BepInEx/config/bnl.community.fov.cfg
```

Edit it to change FOV, weapon model FOV, and ADS sensitivity.
