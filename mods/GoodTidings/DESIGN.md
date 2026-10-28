# Good Tidings: design

Good things that happen at your base, on the same timer as raids: when the game's raid timer says "raid now", a share of the time
(Visit Chance) a good visit comes instead.

## Wandering trader

A Dverger merchant riding a Lox with saddle bags.

**Look** (chosen 2026-10-28 with `goodtidings look ...` previews; built by `src/DvergerLook.cs`)
- Rider: a Dverger body with Hildir's face (her own head bones on the Dverger's head and neck: on the Dverger's face bones it came out
  pulled out of shape), white braids (`DvergerHairFemale`; matches her blonde hair, which is part of her face), no beard, Ashlands
  clothes (`DvergerSuitArbalest_Ashlands`, its pointy hat shrunk away). Two versions, shown at random:
  - **Hooded:** the hood from `DvergerSuitArbalest`, scaled 1.1 from the head so her hair doesn't poke through, and the cape; both black.
  - **Bare:** no hood, no cape (the cape is cut out through the texture: our copy of it is see-through at x 64-128, y 96-128).
  - Clothes ochre in both (recoloured on our texture copy: hue 0.11, strength at least 0.55, brightness x1.2; the buckle, x 108-128,
    y 0-20, keeps its own colour). `goodtidings look trader` shows the two.
- Pose: the Dverger's animations are all crossbow ones (even its idle). For standing, use Hildir's (same skeleton).
- Riding (chosen 2026-10-28, `goodtidings look ride 0.1 0.2`): the player's `Riding_Idle` clip (human rigs share clips), on the Lox's
  `attachpoint`, 10 cm up and 20 cm back from it (along the Lox, not the seat's tilted axes). The rider follows the point each frame
  as the game moves a riding player (`Player.UpdateAttach`); parented to it she came out stretched (the point is scaled about 1 x 2 x 2).
- Trader's name still to choose.
- Mount: the game's Lox (its look only) with its saddle shown, golden (chosen 2026-10-28: the creature shader's own shifts, hue +0.05,
  saturation +0.3, brightness +0.12, on copies of its materials; `goodtidings look ridecolours` shows red and golden).
- Saddle bags: our own model (`model/saddlebag.py` -> `src/SaddleBagModel.cs`, built by `src/SaddleBag.cs`): rough, puffed boxes (each
  face bulged, edges wobbling) in two patches of the Lox saddle's leather atlas, iron buckles and rings.
- Goods on the Lox (game props, `src/Goods.cs`): the long Dverger rune crate, a small crate, a keg, a rolled Lox pelt, a Dverger lantern
  on a pole, clay pots on both sides, a Dverger tankard, and the bags. Each placed by the user in F1 ("90 - Goods Placement", sliders that
  move them live) on 2026-10-28; those values are the defaults in Goods.cs. The placement settings are a tuning aid: drop or hide them
  before release.

**Animation and voice** (0.2.4, the user: "working amazing")
- Riding_Idle on the whole body; Hildir's Greet/Talk/Buy/Sell on a second layer masked to head, arms and fingers (fade in 0.25 s, out
  0.4 s). Her face (jaw, brows, cheeks: bones the human rig doesn't know) copied each frame from an invisible copy of Hildir's skeleton
  running her own animator. Hildir's shop sounds (her Trader's effect lists) with our own lines: greet at 6 m, goodbye past 9 m, start
  trade on E, buy, sell. Gestures and voice are on the trading player's game only.

**Behaviour**
- Arrives at the base, stays for Stay Minutes, then leaves. The time it leaves is saved on the visitor, so it still goes after a restart.
  Built (0.2.x): prefab goodtidings_trader (a Lox copy, TraderPrefab.cs), TraderAI (BaseAI subclass: wander round home, stand while
  someone trades, walk away and despawn), TraderVisitor (dresses it on each game, the shop on E: a switched-off Trader + StoreGui).
- The Lox wanders around the base while nobody is trading; it stands still while someone is.
- Trader and Lox can't be hurt and don't react to hits.
- Pays and charges in coins, through the game's own trader window.

**Stock** (prefab names checked in the game)
- Always: some of Haldor's goods: `FishingRod`, `FishingBait`, `BarrelRings`.
- Ready-made meads (not the bases): `MeadHealthMinor`, `MeadStaminaMinor`, ... more after later bosses.
- Unlocked by bosses (global keys), for example after the Queen (`defeated_queen`): `GemstoneBlue` (Iolite), `GemstoneGreen` (Jade),
  `GemstoneRed` (Bloodstone).
- Ingredients that are hard to gather (list to choose).

**Open questions**
- Which Dverger, and the trader's name.
- The exact stock and prices for each boss stage.
