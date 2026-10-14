# Heave Ho — v1.8

Sit on a bench and row. Shift your stroke with **W/S** just like the helm, and watch oars sweep and splash at every seat. More rowers mean more speed (with diminishing returns), rowing costs stamina, and each oar pushes from its own side, so a crew can steer by rowing.

## Controls (seated on a rowing bench)
| Key | Stroke |
|---|---|
| (sit down) | **Oars up**: default, no rowing, no stamina |
| W | Oars up → Easy stroke → Pull hard |
| S | Pull hard → Easy stroke → Oars up → Back water |
| Jump | Stand up (stroke resets) |

- **Easy stroke**: half thrust, low stamina. **Pull hard**: full thrust, full stamina. **Back water**: reverse, low stamina.
- Oars alone can move a stopped ship with the sail stowed.
- Rowing from one side turns the ship. Port pulling and starboard backing water spins it in place.
- The helm's speed arrows float beside your oar while you row (one arrow = Easy, two = Pull hard, back arrow = Back water).

## Ships
| Ship | Oar visuals | Oar physics |
|---|---|---|
| Raft | — (no seats) | — |
| Karve | on | on |
| Longship | on | on |
| Drakkar | **off** | on |

The Drakkar's crew rows with no visible oars until it gets a hand-placed oar layout (planned). Modded ships with sitting benches get oars automatically, placed by measuring their rail.

## Settings (F1)
Tick **Advanced settings** at the top of the menu to see the fine-tuning options marked *(adv)*.

| Section | What's in it |
|---|---|
| **1. Rowing** | Rower strength, Easy / Back water thrust, Turning strength · *(adv)* Max rowing speed, Rower bonuses |
| **2. Stamina** | Stamina per second, Easy / back water stamina, Resume rowing at |
| **3. Oars (all ships)** | Show oars, Oar size, Stroke rate · *(adv)* Placeholder oar length, Use Valheim shader |
| **4. Splashes** | Oar splashes, Effects on blade entry / exit · *(adv)* Wake splash amount |
| **5. Rower HUD** | Distance outboard, Height |
| **6. Ship: <name>** | Oar visuals, Oar physics, Oar size, Oarlock placement and its sliders · *(adv)* Rowing seat names |
| **9. Advanced** | *(adv)* Rowing seat animations, Render dump key |

- Each **6. Ship** section appears the first time you meet that ship in a session.
- **Oar visuals / Oar physics** are independent. Physics only = an invisible crew still rows; visuals only = cosmetic rowing; both off = ordinary seats.
- **Oarlock placement**: *Fixed* uses one distance and height for every bench (Karve, Longship). *Per seat* measures the rail beside each seat (for hulls whose benches span the beam) and spreads oars that share a bench.
- Almost everything applies live. Seat names, placement, and the shader apply to ships loaded after the change.
- Settings saved by earlier versions are carried over automatically.

## Custom oar and icon
Next to `HeaveHo.dll`:
- **`oar.obj`** + its texture (`oar.jpg` / `oar.png`, or whatever `oar.mtl` names): the oar model. Blender export: File → Export → Wavefront (.obj), Forward -Z, Up Y, Selected Only. Model the blade along +X with the origin at the oarlock, in meters.
- **`oarIcon.png`**: replaces the helm wheel in the rowing HUD (transparent background).
- Alternative to the OBJ: an asset bundle `heaveho` with a prefab `HeaveHo_Oar` (see `UnityEditor/BuildBundles.cs`; build with Valheim's exact Unity version).

Without these files you get a simple built-in placeholder oar.

## Multiplayer
- Every player needs the mod. Each client animates the oars and splashes; the ship's owner (usually the helmsman) applies the thrust.
- Rowers ping the owner every second. A rower who disconnects drops out within about 3 seconds.
- **The server decides the settings.** Joining a server that has Heave Ho, you play by the server's values for everything (strength, thrust, stamina, turning, oar size and placement, seats, splashes' effects, HUD position); your own config file is untouched and comes back when you leave. They show read-only in Configuration Manager. The host and server admins (adminlist.txt) can change them from their own game, and a dedicated server's owner can edit the .cfg while it runs.
- Only what you *see* stays yours: **Show oars**, each ship's **Oar visuals**, **Oar splashes** and the debug key.
- A server without the mod sends nothing, so your own values apply.

## Build
1. Set `ValheimDir` / `BepInExDir` in `HeaveHo.csproj` (or point `BepInExDir` at your r2modman profile).
2. `dotnet build`. The dll is copied to `BepInEx/plugins/HeaveHo/`.

## Troubleshooting
- Every ship's seats, oarlock measurement and settings are logged to `LogOutput.log` the first time it loads (search `[Karve]`, `[VikingShip]`, `[VikingShip_Ashlands]`).
- Water effects you can use for splashes are logged under `[Splash]`.
- *(adv)* Render dump key (F8) writes `BepInEx/HeaveHo_render_dump.txt` if oars look wrong.
