# Weapon Oils (work in progress)

Oils that coat a melee weapon for a while, like meads do for the player. Made in two steps, like mead: a base at the **cauldron**,
then finished at the **oil press**, a new piece in the game's own style. New oils open up as you move through the biomes.

## Decisions
- **Oil press:** a new build piece cloned from the fermenter (the game's own models and materials, so it looks like it belongs), with
  its own station name (`$nick_oilpress`, never the fermenter's). The base goes in with a finishing ingredient, waits, and the finished
  oil comes out, as with mead.
- **Timed:** an oil lasts a set time (setting, 20 minutes by default), counted in world time, shown on the weapon's tooltip.
- **One at a time:** a second oil is refused until the first wears off. Nothing is used up when it is refused.
- **Uneven on purpose:** every biome gets at least one oil, and gets more where its creatures and plants suggest them. Oils are made
  from what their own biome gives you, and a boss kill (setting) opens each biome's recipes.
- **Melee only** for now.

## Oils (draft)

| Biome | Oils | Opens after |
|---|---|---|
| Meadows | Ember (fire), Keen (physical) | start |
| Black Forest | Troll Fat (cheaper swings), Barkhide (slower wear) | Eikthyr |
| Swamp | Venom (poison), Leech (life steal) | The Elder |
| Mountain | Rime (frost) | Bonemass |
| Plains | Storm (lightning), Lox Fat (stagger) | Moder |
| Mistlands | Wisp (spirit), Royal (steadier guard) | Yagluth |
| Ashlands | Cinder (fire, strongest) | The Queen |

`OilDefs.cs` holds the table (ingredients, amounts, gates). Numbers are a first guess; every ingredient's prefab name has been checked
against the running game (2026-10-17).

- **State:** the oil and its end time live on the weapon (`OilState.cs`, `m_customData`, keys prefixed `WeaponOils.`).
- **Applying:** use the oil from the bag on the equipped melee weapon. Animation: right hand holds the weapon out at an angle, the left
  hand sweeps along it from the hilt to the tip.
- **Effect:** a visible glow/drip on the weapon while oiled, in the oil's colour.

## Built so far

Scaffold only: plugin, settings (server-synced strength/duration), the oil table, per-weapon oil state (with the one-oil rule),
translations file. No items, recipes, press, animation or effects yet.

## To do
1. The oil press piece (fermenter clone, `$nick_oilpress`, with a build recipe and its conversions), then the base items at the
   cauldron and the finished oils.
2. Apply: use-item patch, the effects (damage types, stamina, durability, life steal, stagger, guard), message and sound.
3. Carry the oil's `m_customData` keys over when a weapon is upgraded (`InventoryGui.DoCrafting`; see valheim-pitfalls).
4. Apply animation (IK on the player's hands) and the weapon effect.
5. Translations in the 8 other languages, `modcheck`, real restart.
