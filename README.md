# BNL Community Launcher

One-click mod for Block N Load that connects you to the community server — no more "No servers available" screen. Safe for Steam, no game files modified.

> **Status**: Working — community servers online, EAC bypassed.

## Installation

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

## Uninstalling

Delete the `winhttp.dll` file from `BlockNLoad\Win64\`. That's it — the game goes back to vanilla.

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
