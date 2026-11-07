# Lantern Frame

Lets you place a **lantern** on the **horizontal item frame** (`itemstandh`) in Valheim.

The lantern is shown on the frame with its model and its warm light.

## Installation

Install with r2modman / Thunderstore Mod Manager, or drop `LanternFrame.dll` into `BepInEx/plugins`.
Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

## Multiplayer

Install on every player that wants to place or see the lantern: the client decides whether an item fits the frame.

## Configuration

`BepInEx/config/user.lanternframe.cfg` (created after the first launch):

| Setting | Default | Description |
|---|---|---|
| `AllowedItems` | `lantern` | Comma-separated, case-insensitive fragments. Any item whose prefab name or shared name contains one of them can be placed on the horizontal frame. |
| `LogAttachAttempts` | `false` | Logs prefab names and what gets spawned on the frame. Useful for adding other items. |

## Known limitations

- The lantern's model is hung from its hand-hold, so its orientation on the frame may not be perfect.
- Its light keeps flickering while on the frame.

## Source

See the GitHub repository for source and issues.

## Building

Requires the .NET SDK, Valheim, and a BepInEx profile (r2modman).

```powershell
.\build-package.ps1 -ValheimDir "C:\Program Files (x86)\Steam\steamapps\common\Valheim" -ProfileDir "<your r2modman profile>"
```
The Thunderstore zip is written to `dist\`.
