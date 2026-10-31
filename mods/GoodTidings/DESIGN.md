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
- The stock file (0.9.0, `src/StockFile.cs`): `BepInEx/config/nick.GoodTidings.stock.json`, written with help the first time. `add`
  (item, slot goods/meads/rares/always, land number or name, stack, price), `remove` (prefab names, built-in or found too),
  `findOtherMods`. The server's file applies to everyone (ServerSettings text "Stock"); a saved edit applies at once (watched).
- Other mods' items (0.9.0, `src/StockDiscovery.cs`, Wanted's way): the game's own items are noted at ObjectDB.Awake before mods add
  theirs (after a hot reload: "$item_" names count as the game's). Anything else that is gathered (vegetation pickables, rocks, trees) or
  dropped by a spawn-list creature becomes goods of the earliest land it comes from (a "defeated_X" spawn key raises it); a fermenter's
  product whose base is made of placed things becomes a mead of its latest ingredient's land. Priced as the middle of that land's
  built-in goods or meads. Weapons, armour and tools are never found. `goodtidings found` lists them (WeaponOils: its 12 oils).

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
Riddles (0.7.x, both outcomes tested by the user): generated in Riddles.cs from the player's own discoveries (known recipes,
materials, stations; the game's drop tables and vegetation): what a recipe makes, who drops a thing (only things one creature drops),
where a thing is made, what wakes a beaten boss, which one land grows a thing; wrong answers of the same kind; untranslated names skipped.
The window (RiddleWindow.cs, GameUi template) shows the riddle and three answers; the answer goes to his owner by RPC, the first counts
(ZDO Answered), everyone near sees who answered; he says a parting line and leaves 5 s later. Right: two of the newest land's goods
(Stock.Reward, 1 in 4 its rare) dropped at his feet. Wrong: 3 (4 from the Plains on) of the newest land's creatures hunt the base from
22-30 m. Old riddles (0.8.0, Lore.cs): 26 hand-written, from the Eddas and Valheim's story; the server picks one when he lands
("Old Riddle Chance", 35%), the same for everyone; a global key per riddle remembers which were asked in the world, none repeats
until all have been (then they start over); the window says "an old riddle"; a right answer always brings the land's rare.
Harder as the world learns (0.8.1): the world's wisdom = riddles answered rightly (global keys goodtidings_wisdom_1..10); made riddles
offer 3 answers, 4 from 5 right, 5 from 10 (never more wrong answers than the player has discovered); the reward gains one good per 5
right answers (at most two). Old riddles keep their 3 hand-written answers.
Earlier plan: generated "do you remember" riddles from the player's own discoveries (endless, contextual, fair); rare hand-written
lore riddles (each world remembers which were asked); harder as more are answered. Right: a reward from the current land. Wrong: he
takes flight with a cry and the land's beasts answer it (ravens and wolves hunt together; carrion birds follow battle).

## Muninn's reward: what the raven saw (0.16.x, set with the user 2026-10-31)

Items were the trader's kind of reward, so a right answer now gives knowledge (RavenSaw.cs). The server (it alone knows every place
and keeps the world's count of right answers in the world key "goodtidings_ravens N") sends the candidates; each player within 40 m of
him marks the first one their own map hasn't explored (a saved pin, 150 m revealed): every right answer the nearest dungeon of this
land or the next (burial chambers, sunken crypt, frost cave, fuling village, infested mine, charred fortress); every third a valuable
one, the next boss's altar (Boss pin) or a trader (Haldor, Hildir, the Bog Witch from the Swamp on). Nothing left to show: a Battle or
Protection Idol of the newest land (Upgrader{land}Weapon/Armor, the Forge of Potential's) and the land's rare, into the inventory. An
old riddle answered rightly also gives "The Raven's Road" (Ravens Road Minutes 10: portals take metals, Humanoid.IsTeleportable).
Wrong answers: the beasts as before, now marked like the Dvergr's kin (DvergerBand.Mark): a red circle on the base while they're
about, gone after 8 minutes, their own drops. Testing: `goodtidings ravensaw [lore] [test] | gift`, `goodtidings places`, `idols`.

## The Wounded Dvergr (0.9.1-0.11.x, set and tested with the user 2026-10-30)

A neutral visit, with a reward that isn't items (the user wanted each visit's good to be its own kind: Dagny trade, Bountiful time,
Muninn knowledge, the Dvergr craft). Our own saved object goodtidings_dverger (Dverger copy, DvergerAI instead of MonsterAI, NpcTalk and
drops removed, untouchable like Dagny). Map: "?" and the yellow circle, as Muninn.
- Arrives 35-45 m out, staggers in (player's "Encumbered walk") and goes down on one knee 10-18 m from the base ("Kneel Start", then
  "Kneel Loop": the user chose a pose with motion over KneelDeath).
- E helps him up. He goes to the burning fire nearest him within 60 m (fires found through the game's warmth areas, EffectArea Heat,
  only on while burning: torches don't count): a free bed within 6 m of it (lies, "Laying Sleeping"), else a seat (chair "Player_Sit_Chair",
  throne "Sit_Throne_Idle"), else the ground beside it ("Sitting idle"). A bed or seat counts only with a free floor spot beside it that
  his pathfinding reaches (no reaching it through a wall); after a minute short of it he goes to the ground by the fire instead. Doors
  in his way are opened when he stops getting closer. With no fire he sits where he is, too cold to eat, and goes once one is lit.
- Walking uses the player's "Walk New"/"IdleTweaked" (the Dverger's own walk holds a crossbow), mixed by speed (DvergerMotion).
  Borrowed clips are copies without events (the death clip's "Die" event killed him).
- Resting he counts as attached (Character.IsAttached patch), no gravity, his collider a trigger, his look direction the way he lies
  (standing still the game turned him to his last look direction against the hold: he shook).
- Fed from the hotbar (anything with food value; Meals 3; the player's eating sound). Healed: rests 8 s, walks to the nearest player,
  works at their gear ("Workbench" animation with the workbench's crafting sounds, 4.5 s), then everyone within 30 m gets all gear
  repaired and "Dvergr-tempered" (Tempered.cs: our SE_Stats, hammer icon, Tempered Minutes 30; wear at Tempered Wear 50%, through
  Game.m_durabilityRate around the game's wear methods and a refund on armour).
- Not fed in Stay Minutes (10): he limps off cursing; 45 s later his kin come (DvergerBand.cs): 2 rogues, a mage from the Mountains on
  (fire or ice from the Mistlands), another rogue from the Ashlands on, 26-32 m out, aggravated; health and damage scaled to the newest
  land (0.25 Meadows ... 1.0 Mistlands ... 1.6 Deep North); they drop only coins (10 Meadows ... 85 Deep North, +-25%); a red raid
  circle while any are about; gone after 8 minutes.
- Looks (WoundedLook): the Ashlands outfit without hood and cape, grey at brightness 1.05 (user's pick), red beard.
- Testing: `goodtidings visit dverger`, `goodtidings dverger leave` (time up now), `goodtidings dverger fires`, `look wounded`,
  `look woundedcolours`.

## The Skald (0.11.5-0.13.5, set and tested with the user 2026-10-30)

A positive visit, its reward skill (and comfort). Decided with the user 2026-10-30:
- He's a friendly skeleton: a player body made into bones with DummyMe's SkeletonBody (copied as SkaldBones.cs, the user's DummyMe
  values built in: Plain, skull 0.81 at -2.982/5.527/0.405 cm, eyes 1 cm, shoulders 1.1), so player clothes fit him (SkaldBones.Wear:
  attach_skin meshes on the same bones; hats on Helmet_attach, not the head bone, or they float above, grown). Capes don't work on
  him (cloth): none. Outfit not chosen yet (`look skaldwear me` copies what the player wears).
- Plays a Mixamo animation ("Guitar Playing", reference/, standing): Blender 5.0 headless turns the FBX into data (assets/skald_play.json:
  rest T-pose and each frame's bone turns, already in Unity's axes: x' = -x, y' = z, z' = -y); Retarget.cs rebuilds the skeleton
  invisibly, makes it a humanoid Avatar (AvatarBuilder) and copies each frame onto his body with HumanPoseHandler. No Unity editor needed
  (Valheim is Unity 2022.3.62f1; bundles from the user's Unity 6 wouldn't load).
- His lute (SkaldLook.cs): built from primitives in a wooden item's material (the club's; building pieces' shader showed white),
  grain scaled 4x. Held: fixed to his lower spine at the "Held" settings from his hips (the user's values are the defaults: forward
  0.123, side 0.08, up 0.3, tilt 44.563), the neck aimed at his fretting hand's average spot (learnt over the first loop) moved by
  "Neck point" (side 0.053, up 0.03). Smoothing toward the live hand floated; fixed is right. On his back (BackShield_attach) while
  he rises; that fit isn't tuned yet. Settings F1 "98 - Skald Lute" (local, testing).
- His songs (0.14.0): three made by the user with ElevenLabs (paid plan: they may ship in the mod; Thunderstore's AI disclosure applies):
  Lyre of the North, Northwind Lyre, Runes of the Lyre (reference/*.mp3 -> assets/skald_song1-3.ogg, mono Ogg Vorbis 96 kbps by Blender's
  aud). Embedded, written to BepInEx/cache, loaded with UnityWebRequestMultimedia. Shuffled per visit (seed: his ZDO id), played back
  to back from the server clock (the time he began), so everyone hears the same song at the same moment. 3D, the game's sfx mixer group,
  Song Volume (each player's). The game's music is lowered near him (Music Near Skald, each player's, 20%: lowest within 10 m, full
  at 25 m) through MusicVolume.UpdateProximityVolumes, which the game fades to.
- Outfit (the user's): Celebratory Cap, Bronze Plate Tunic, Bronze Plate Leggings (SkaldVisitor.Outfit).
- 0.15.x (set with the user): his lyre is the user's own Meshy model (reference/Meshy_AI_Dragonhead_Lyre zip; tools/convert_lyre.py:
  Unity axes, origin at the soundbox, a fingerboard added behind the strings, texture 1024 px; assets/skald_lyre.bin/.png, built as
  a mesh at run time; LoadImage called by name: its module targets a newer .NET). Lyre size 0.9; on his back at -0.013/-0.028/-0.131 m
  turned 84/17/26 (the back-shield point has a scale of 95: offsets are divided by it). The stages, in his ZDO and moved on by his
  owner: rising (3.5 s) -> wandering (20 s of strolls within 4 m, each checked clear both ways by body-wide sweeps, a look-ahead each
  step; then back to his spot and a steady turn to face the open room: OpenWay, 16 rays, leaning toward where the player stood) ->
  playing (his three songs once each, shuffled: their lengths are in the code, a dedicated server loads no audio) -> bowing (the
  user's Mixamo "Quick Informal Bow", played once) -> sinking. Changes blend (PoseBlend, MixamoPlayer.BlendFrom/FadeOut); the lyre
  glides on and off his back. Lines over his head with Haldor's sounds (sfx_haldor_greet/yea/laugh): risen, begin, between songs, bow.
  Listeners also get "Remembered in Song" (0.15.3, Remembered in Skald.cs): our own status with the game's no-skill-drain icon, No
  Skill Drain Minutes 30; a death while it's on skips Skills.OnDeath. (The game's own SoftDeath can't be given: each frame it resets its
  time from the player's last death, so it ended at once.) Stay Minutes is gone.
  The body's animator updates every frame (0.15.4): on the physics clock the blends read their own last output back on skipped frames
  and he shook between animations. `goodtidings visit skald demo`: a short visit in front of the player (6 s strolling, 5 s playing).
- The visit (Skald.cs): saved object goodtidings_skald (empty, a trigger to look at, as Muninn's). He takes the most comfortable spot
  within 20 m of the base (the game's own SE_Rested.CalculateComfortLevel with Cover's shelter check, a 2 m grid, floors found by a ray
  down from just above the base's height, room to stand; equals: the nearest), facing where the player stood. Rises with the
  skeletons' "Skeleton Rise" (events stripped) and the summoned-skeleton ground effect and rise sound, lute on his back; plays
  (Stay Minutes 6); sinks with the rise backwards. Changes blend (MixamoPlayer.BlendFrom / FadeOut on HumanPose muscles, 0.8 s);
  the lute glides from his back to his hands. While playing: golden motes and a warm light; +Comfort 2 within 10 m
  (SE_Rested.CalculateComfortLevel postfix); a player within 12 m for Listen Seconds 30 gets every skill +5% of its level (at least
  0.5), once a visit (remembered in Player.m_customData), with the game's skill level-up effect. Map: blue circle and "?".
  Song Volume is each player's own setting.
- LocalFx: the game's effect prefabs are networked; made as they are on every game they'd duplicate for everyone. LocalFx makes them
  with ZNetView.m_forceDisableInit and strips the network parts (this fixed Bountiful Season's glow, which had the same bug).
- Testing: `look skald`, `skaldbones`, `skaldviking`, `skaldoutfits`, `skaldhats`, `skaldwear items | items`, `skaldwear me`,
  `skaldplay`, `skaldback`; `lute`, `luteback`; `goodtidings visit skald`.


**Open questions**
- Which Dverger, and the trader's name.
