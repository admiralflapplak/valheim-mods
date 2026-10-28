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

**Dagny and Atlas** (0.3.x): the trader is Dagny (a nod to Atlas Shrugged: "value for value" lines), her golden Lox is Atlas ("Atlas
shrugs" when petted). Riding idle/walk/canter blended by his speed. While someone trades he turns side-on to them. E towards his head
pets him; towards her, trades. He eats food dropped near him (Lox food and the early berries and roots); each meal knocks 30% off one
random item she can sell now, up to three a visit, saved on her ZDO. The goods are strapped to his hip bone, so they rock with him.

**Behaviour**
- Arrives at the base, stays for Stay Minutes, then leaves. The time it leaves is saved on the visitor, so it still goes after a restart.
  Built (0.2.x): prefab goodtidings_trader (a Lox copy, TraderPrefab.cs), TraderAI (BaseAI subclass: wander round home, stand while
  someone trades, walk away and despawn), TraderVisitor (dresses it on each game, the shop on E: a switched-off Trader + StoreGui).
- The Lox wanders around the base while nobody is trading; it stands still while someone is.
- Trader and Lox can't be hurt and don't react to hits.
- Pays and charges in coins, through the game's own trader window.

**Stock** (set with the user 2026-10-28, `src/Stock.cs`)
- 15 lines a visit, rolled from a seed saved on her (every player sees the same): 1-3 always (fishing rod, bait 20 for 10, barrel hoops
  3 for 100, Haldor's prices); 4-13 random from every unlocked land's goods and meads, a land weighted by (its place)^2 so newer lands
  dominate; 14 random from the newest land; 15 the newest land's rare.
- Lands by boss key: Meadows (start), Black Forest (eikthyr), Swamp (gdking), Mountains (bonemass), Plains (dragon), Mistlands
  (goblinking), Ashlands (queen), Deep North (fader). Goods follow the user's lists; meads placed by where they're made (the Bog Witch's
  Troll Endurance and Anti-Sting in the Swamp).
- Ordinary lines 3 buys a visit, the rare 1, shared (her ZDO "Sold", counted by her owner via RPC). Atlas's discounts only on lines in
  this visit's stock that aren't sold out.

## Bountiful Season (0.5.x, set with the user 2026-10-28)

For 5 minutes (setting) everything within 96 m of the base (a raid's range; the map circle drawn as a raid's, 0.9 of it) works faster, including what's started meanwhile: smelters, kilns,
fermenters and cooking stations at 8x (setting "Speed"), crops, saplings and beehives at 20x ("Growing Speed": they take far longer;
the user picked this over a head start, which stays as a setting defaulting to 0). Each piece's own "last updated" clock is moved back
on the game that owns it (the game's catch-up for time nobody was near): fuel used as usual, cooking boosted only up to done, never
burnt. Look: the boss-summon particles (vfx_prespawn) slowed and tinted wispy green over each piece, 3 m apart; Elder-staff grow sound;
a blue circle on the map. Tested: kiln and smelter loads done in under a minute, fermenter done, beehive full, beech and birch grown
(oak needs ~27x). While one of our visits is going on, a raid roll is skipped.

## Muninn's Riddle (0.6.x, in progress)

A neutral visit: Odin's white raven of memory (the user chose Muninn; lore: memory, riddle contests like Vafthrudnismal) lands on open
ground 6-16 m from the base and asks one riddle, first come first served. Our own saved object goodtidings_muninn; each game builds his
look from a copy of the tutorial's Munin (their tutorial birds untouched; copies need the controller's "flyin" trigger, they start
"away"), turns him to the player with the controller's "anglevel". Map: the visit circle in yellow (neutral; blue = good) and the
game's "?" (mapicon_hildir1, as Hildir marks treasure places), no name. Leaves after Stay Minutes. Raids are skipped while he's there.
Riddles (next): generated "do you remember" riddles from the player's own discoveries (endless, contextual, fair); rare hand-written
lore riddles (each world remembers which were asked); harder as more are answered. Right: a reward from the current land. Wrong: he
takes flight with a cry and the land's beasts answer it (ravens and wolves hunt together; carrion birds follow battle).

**Open questions**
- Which Dverger, and the trader's name.
- The exact stock and prices for each boss stage.
